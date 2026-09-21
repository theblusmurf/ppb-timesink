using System.Numerics;

namespace PoteHunter;

public readonly record struct RangedAimCalibration(double BodyRadiansPerPixel,double CameraYawRadiansPerPixel,double CameraPitchRadiansPerPixel)
{
    public bool Valid =>
        double.IsFinite(BodyRadiansPerPixel) && Math.Abs(BodyRadiansPerPixel) >= 1e-5 && Math.Abs(BodyRadiansPerPixel) <= .05 &&
        double.IsFinite(CameraYawRadiansPerPixel) && Math.Abs(CameraYawRadiansPerPixel) >= 1e-5 && Math.Abs(CameraYawRadiansPerPixel) <= .05 &&
        double.IsFinite(CameraPitchRadiansPerPixel) && Math.Abs(CameraPitchRadiansPerPixel) >= 1e-5 && Math.Abs(CameraPitchRadiansPerPixel) <= .05;
}

public sealed partial class Movement
{
    double cameraYawRadiansPerPixel;
    double cameraPitchRadiansPerPixel;
    double calibratedBodyRadiansPerPixel;
    public double TargetHeightOffset { get; set; } = 1;
    long lastCameraTrace;
    Vector3 previousTraceCamera,previousTraceTarget;
    uint previousTraceTargetId;
    (uint Id,uint Generation,long Address) smoothedAimTarget;
    bool hasSmoothedAimTarget;
    long smoothedAimAt;
    CameraError smoothedOpticalError;

    public bool CameraCalibrated =>
        double.IsFinite(cameraYawRadiansPerPixel)&&Math.Abs(cameraYawRadiansPerPixel)>=1e-5&&
        double.IsFinite(cameraPitchRadiansPerPixel)&&Math.Abs(cameraPitchRadiansPerPixel)>=1e-5;

    public RangedAimCalibration ExportRangedAimCalibration()
    {
        var calibration=new RangedAimCalibration(RadiansPerPixel,cameraYawRadiansPerPixel,cameraPitchRadiansPerPixel);
        if(!calibration.Valid)throw new InvalidOperationException("The completed ranged calibration was invalid.");
        return calibration;
    }

    public static Movement RestoreRangedCalibration(World world,RangedAimCalibration calibration)
    {
        if(!calibration.Valid)throw new InvalidOperationException("Saved ranged aim calibration is invalid.");
        if(!world.CameraSupported)throw new InvalidOperationException(world.CameraStatus);
        _=world.ReadCamera(); // Verify that the live camera is readable before enabling input.
        var current=world.LocalPlayer();
        return new Movement
        {
            RadiansPerPixel=calibration.BodyRadiansPerPixel,
            cameraYawRadiansPerPixel=calibration.CameraYawRadiansPerPixel,
            cameraPitchRadiansPerPixel=calibration.CameraPitchRadiansPerPixel,
            calibratedBodyRadiansPerPixel=calibration.BodyRadiansPerPixel,
            Forward=FromClientHeading(current.Heading),
            UnitsPerMs=0
        };
    }

    public async Task CalibrateCamera(World world,CancellationToken token,Action<string,object>? trace=null,Action<string>? status=null)
    {
        if(!world.CameraSupported)throw new InvalidOperationException(world.CameraStatus);
        const int pulse=36;
        try
        {
            Input.Release();
            await Input.Delay(150,token);
            var owner=world.LocalPlayer();
            AxisCalibration yaw=await CalibrateAxis(world,owner,"horizontal",pulse,vertical:false,token,trace,status);
            await Input.Delay(150,token);
            AxisCalibration pitch=await CalibrateAxis(world,owner,"vertical",pulse,vertical:true,token,trace,status);

            cameraYawRadiansPerPixel=yaw.RadiansPerPixel;
            cameraPitchRadiansPerPixel=pitch.RadiansPerPixel;
            calibratedBodyRadiansPerPixel=yaw.BodyRadiansPerPixel;
            trace?.Invoke("3D camera calibration",new{YawRadiansPerPixel=cameraYawRadiansPerPixel,PitchRadiansPerPixel=cameraPitchRadiansPerPixel,
                YawSamples=yaw.AcceptedSamples,PitchSamples=pitch.AcceptedSamples,YawWorstRestoreError=yaw.WorstRestoreError,
                PitchWorstRestoreError=pitch.WorstRestoreError,Character=owner.Name});
        }
        catch
        {
            cameraYawRadiansPerPixel=0;cameraPitchRadiansPerPixel=0;calibratedBodyRadiansPerPixel=0;
            throw;
        }
        finally{Input.Release();}
    }

    public static async Task<Movement> CalibrateRanged(World world,CancellationToken token,Action<string,object>? trace=null,Action<string>? status=null)
    {
        var movement=new Movement();
        await movement.CalibrateCamera(world,token,trace,status);
        var current=world.LocalPlayer();
        if(!double.IsFinite(movement.calibratedBodyRadiansPerPixel)||Math.Abs(movement.calibratedBodyRadiansPerPixel)<1e-5)
            throw new InvalidOperationException("Horizontal camera calibration did not produce a valid character-body response.");
        movement.RadiansPerPixel=movement.calibratedBodyRadiansPerPixel;
        movement.Forward=FromClientHeading(current.Heading);
        movement.UnitsPerMs=0;
        trace?.Invoke("ranged combined calibration",new{movement.RadiansPerPixel,movement.Forward,
            CameraYawRadiansPerPixel=movement.cameraYawRadiansPerPixel,CameraPitchRadiansPerPixel=movement.cameraPitchRadiansPerPixel});
        return movement;
    }

    public async Task<bool> FaceTarget3D(World world,Entity target,CancellationToken token,double tolerance=.01)
    {
        RequireCameraCalibration();
        if(!double.IsFinite(tolerance)||tolerance<=0||tolerance>.25)throw new ArgumentOutOfRangeException(nameof(tolerance));
        var owner=world.LocalPlayer();
        CameraFrame camera=FreshCamera(world,owner);
        Vector3 aim=TargetPoint(target);
        CameraError opticalError=Error(camera,aim);
        Size viewport=world.CameraViewportSize;
        var precision=CameraAimPrecision.Evaluate(camera,aim,viewport.Width,viewport.Height);
        if(Math.Abs(opticalError.Yaw)<=tolerance&&Math.Abs(opticalError.Pitch)<=tolerance&&precision.Aligned)return true;
        // The memory position stays authoritative for hit precision and stop
        // decisions. Only small, same-target controller errors are filtered so
        // camera correction remains smooth without lagging a new/moving target.
        CameraError stabilizedError=StabilizeAim(target,opticalError);
        CameraError controlError=ControlError(camera,owner,aim,stabilizedError,out bool pivotValidated,out double pivotRayMiss,out double parallaxMultiplier);
        int x=Pixels(controlError.Yaw,cameraYawRadiansPerPixel,96,ControlGain(controlError.Yaw));
        // A target behind the camera needs a coarse yaw turn before vertical
        // feedback has a stable screen-space meaning.
        int y=opticalError.Depth>0?Pixels(controlError.Pitch,cameraPitchRadiansPerPixel,144,VerticalControlGain(controlError.Pitch)):0;
        Input.Aim(x,y,token);
        TraceCamera("face target 3D",camera,target,opticalError,controlError,pivotValidated,pivotRayMiss,parallaxMultiplier,x,y,precision,
            stabilizedError.Yaw!=opticalError.Yaw||stabilizedError.Pitch!=opticalError.Pitch);
        await Input.Delay(25,token);
        return false;
    }

    public async Task<bool> AimVertical3D(World world,Entity target,CancellationToken token,double tolerance=.01)
    {
        RequireCameraCalibration();
        if(!double.IsFinite(tolerance)||tolerance<=0||tolerance>.25)throw new ArgumentOutOfRangeException(nameof(tolerance));
        var owner=world.LocalPlayer();
        CameraFrame camera=FreshCamera(world,owner);
        Vector3 aim=TargetPoint(target);
        CameraError error=Error(camera,aim);
        if(error.Depth<=0||Math.Abs(error.Pitch)<=tolerance)return error.Depth>0;
        CameraError stabilizedError=StabilizeAim(target,error);
        CameraError controlError=ControlError(camera,owner,aim,stabilizedError,out bool pivotValidated,out double pivotRayMiss,out double parallaxMultiplier);
        int y=Pixels(controlError.Pitch,cameraPitchRadiansPerPixel,96,VerticalControlGain(controlError.Pitch));
        Input.Aim(0,y,token);
        TraceCamera("vertical target 3D",camera,target,error,controlError,pivotValidated,pivotRayMiss,parallaxMultiplier,0,y);
        await Input.Delay(25,token);
        return false;
    }

    static CameraFrame FreshCamera(World world,Entity owner)
    {
        CameraFrame frame=world.ReadCamera();
        var current=world.LocalPlayer();
        if(!LocalCharacter.Same(owner,current))throw new OperationCanceledException("Character changed during camera feedback.");
        Aim3D.ValidateCamera(frame);
        return frame;
    }

    Vector3 TargetPoint(Entity target)
    {
        if(target.Address<0x10000||!target.Position.Finite||!double.IsFinite(target.Height)||!double.IsFinite(TargetHeightOffset)||TargetHeightOffset is <0 or >4)
            throw new InvalidOperationException("The 3D target point is invalid.");
        return new((float)target.Position.X,(float)(target.Height+TargetHeightOffset),(float)target.Position.Y);
    }

    static CameraError Error(CameraFrame camera,Vector3 target)
    {
        Vector3 delta=target-camera.Position;
        if(!float.IsFinite(delta.X)||!float.IsFinite(delta.Y)||!float.IsFinite(delta.Z)||delta.LengthSquared()<1e-6f)throw new InvalidOperationException("The 3D target direction is invalid.");
        Vector3 forward=Vector3.Normalize(camera.Forward),up=Vector3.Normalize(camera.Up),right=Vector3.Normalize(Vector3.Cross(up,forward));
        double f=Vector3.Dot(delta,forward),r=Vector3.Dot(delta,right),u=Vector3.Dot(delta,up);
        return new(Math.Atan2(r,f),Math.Atan2(u,Math.Sqrt(f*f+r*r)),f);
    }

    CameraError StabilizeAim(Entity target,CameraError actual)
    {
        var identity=(target.Id,target.Generation,target.Address);
        long now=Environment.TickCount64;
        bool reset=!hasSmoothedAimTarget||smoothedAimTarget!=identity||now-smoothedAimAt>250;
        if(reset)
        {
            hasSmoothedAimTarget=true;smoothedAimTarget=identity;smoothedAimAt=now;smoothedOpticalError=actual;
            return actual;
        }
        // Inside the fine aim band use the raw 3D error so picker precision is
        // never delayed. Outside it, smooth ordinary same-direction corrections.
        double yaw=SmoothAimAxis(smoothedOpticalError.Yaw,actual.Yaw);
        double pitch=SmoothAimAxis(smoothedOpticalError.Pitch,actual.Pitch);
        smoothedAimAt=now;smoothedOpticalError=new(yaw,pitch,actual.Depth);
        return smoothedOpticalError;
    }

    internal static double SmoothAimAxis(double previous,double current)
    {
        if(!double.IsFinite(previous)||!double.IsFinite(current))return current;
        if(Math.Abs(current)<=.015||Math.Sign(previous)!=Math.Sign(current)||Math.Abs(current)>=.15)return current;
        return previous*.40+current*.60;
    }

    // The client orbits the eye around this look-at point (UpdateBattleCharacter
    // adds 150 client units, then SetCamera looks at that position).  A camera
    // pixel therefore rotates eye->pivot, not eye->target.  Use that geometry
    // to compensate the measured optical error only when the live ray confirms
    // the model.  Keeping optical error as the signal guarantees that camera
    // shake/offset cannot create a non-zero optical equilibrium.
    static CameraError ControlError(CameraFrame camera,Entity owner,Vector3 target,CameraError opticalError,out bool pivotValidated,out double pivotRayMiss,out double parallaxMultiplier)
    {
        Vector3 pivot=new((float)owner.Position.X,(float)owner.Height+1.5f,(float)owner.Position.Y);
        Vector3 eyeToPivot=pivot-camera.Position;
        Vector3 forward=Vector3.Normalize(camera.Forward);
        double along=Vector3.Dot(eyeToPivot,forward);
        Vector3 closest=camera.Position+forward*(float)along;
        pivotRayMiss=Vector3.Distance(pivot,closest);
        double boom=eyeToPivot.Length();
        pivotValidated=float.IsFinite(pivot.X)&&float.IsFinite(pivot.Y)&&float.IsFinite(pivot.Z)&&
            double.IsFinite(pivotRayMiss)&&along>0&&boom is >=2 and <=30&&pivotRayMiss<=.30;
        double targetDistance=Vector3.Distance(pivot,target);
        parallaxMultiplier=CameraCorrectionMultiplier(boom,targetDistance,pivotValidated);
        return new(opticalError.Yaw*parallaxMultiplier,opticalError.Pitch*parallaxMultiplier,opticalError.Depth);
    }

    internal static double CameraCorrectionMultiplier(double boom,double targetDistance,bool pivotValidated) =>
        pivotValidated&&double.IsFinite(boom)&&double.IsFinite(targetDistance)&&targetDistance>.01
            ?Math.Clamp((boom+targetDistance)/targetDistance,1,8):1;

    internal static double ControlGain(double error)
    {
        double magnitude=Math.Abs(error);
        return magnitude>=.15?.82:magnitude>=.05?.72:.55;
    }

    // Vertical aiming needs to close large elevation gaps quickly, particularly
    // when the character keeps moving forward through a mouse turn.
    internal static double VerticalControlGain(double error)
    {
        double magnitude=Math.Abs(error);
        return magnitude>=.15?.92:magnitude>=.05?.82:.70;
    }

    static int Pixels(double error,double radiansPerPixel,int limit,double gain)
    {
        if(!double.IsFinite(error)||!double.IsFinite(radiansPerPixel)||Math.Abs(radiansPerPixel)<1e-5)throw new InvalidOperationException("Invalid 3D camera calibration. Press F8 again.");
        int pixels=Math.Clamp((int)Math.Round(error/radiansPerPixel*gain),-limit,limit);
        return pixels!=0?pixels:Math.Sign(error/radiansPerPixel);
    }

    static async Task<AxisCalibration> CalibrateAxis(World world,Entity owner,string name,int pixels,bool vertical,CancellationToken token,Action<string,object>? trace,Action<string>? status)
    {
        var accepted=new List<AxisSample>(3);
        int preferredDirection=1;
        for(int attempt=1;attempt<=4;attempt++)
        {
            string activity=vertical?"vertical aiming":"horizontal turning";
            status?.Invoke($"Calibrating {activity} (cycle {attempt}/4)…");
            trace?.Invoke("3D camera calibration axis start",new{Axis=name,Cycle=attempt,Activity=activity});
            await Input.Delay(120,token);
            double start=CameraAngle(FreshCamera(world,owner),vertical);
            double bodyStart=world.PlayerHeading();
            int outwardPixels=pixels*preferredDirection;
            LegDelivery outward=await DeliverLeg(world,owner,name,attempt,"outward",outwardPixels,start,vertical,vertical?2:5,token,trace,status);
            if(vertical&&outward.NoResponse)
            {
                trace?.Invoke("3D camera calibration direction reversal",new{Axis=name,Attempt=attempt,Reason="initial direction was not sampled or was at a pitch limit"});
                outwardPixels=-outwardPixels;
                outward=await DeliverLeg(world,owner,name,attempt,"outward reversed",outwardPixels,start,vertical,5,token,trace,status);
                if(outward.NoResponse)throw new InvalidOperationException("The game sampled no vertical camera response in either direction. Keep focus on the game and press F8 again.");
            }
            if(outward.NoResponse)throw new InvalidOperationException($"The game sampled no {name} camera response after five deliveries. Keep focus on the game and press F8 again.");
            CameraSettlement outwardSettlement=outward.Settlement;
            double outwardChange=AngleDifference(outwardSettlement.Angle,start,vertical);
            if(!outwardSettlement.Settled)
            {
                if(CanDiscardQuietPartial(outwardSettlement,outwardChange))
                {
                    if(vertical)preferredDirection=-Math.Sign(outwardPixels);
                    trace?.Invoke("3D camera calibration quiet partial discarded",new{Axis=name,Cycle=attempt,Leg="outward",
                        Pixels=outwardPixels,AngleDelta=outwardChange,outwardSettlement.Observed,outwardSettlement.QuietMs,
                        outwardSettlement.ChangeCount,NextDirection=preferredDirection});
                    continue;
                }
                status?.Invoke($"Waiting for {name} camera motion to settle...");
                CameraSettlement quietOutward=await WaitForAxisQuiet(world,owner,vertical,token);
                if(quietOutward.Settled)
                {
                    accepted.Clear();
                    trace?.Invoke("3D camera calibration still-moving measurement rebased",new{Axis=name,Cycle=attempt,Leg="outward",
                        QuietMs=quietOutward.QuietMs,NextCycle=attempt+1});
                    continue;
                }
                throw new InvalidOperationException($"The game gave a still-moving {name} outward response; no further calibration input was sent. Keep the mouse still and press F8 again.");
            }
            double bodyTurned=world.PlayerHeading();

            int returnPixels=-outwardPixels;
            LegDelivery returnedLeg=await DeliverLeg(world,owner,name,attempt,"return",returnPixels,outwardSettlement.Angle,vertical,5,token,trace,status);
            if(returnedLeg.NoResponse)throw new InvalidOperationException($"The game sampled no {name} return response after five deliveries. Keep focus on the game and press F8 again.");
            CameraSettlement returned=returnedLeg.Settlement;
            double returnChange=AngleDifference(returned.Angle,outwardSettlement.Angle,vertical);
            double restoreError=AngleDifference(returned.Angle,start,vertical);
            if(!returned.Settled)
            {
                if(CanDiscardQuietPartial(returned,returnChange))
                {
                    trace?.Invoke("3D camera calibration quiet partial discarded",new{Axis=name,Cycle=attempt,Leg="return",
                        Pixels=returnPixels,AngleDelta=returnChange,returned.Observed,returned.QuietMs,returned.ChangeCount});
                    continue;
                }
                status?.Invoke($"Waiting for {name} camera motion to settle...");
                CameraSettlement quietReturn=await WaitForAxisQuiet(world,owner,vertical,token);
                if(quietReturn.Settled)
                {
                    accepted.Clear();
                    trace?.Invoke("3D camera calibration still-moving measurement rebased",new{Axis=name,Cycle=attempt,Leg="return",
                        QuietMs=quietReturn.QuietMs,NextCycle=attempt+1});
                    continue;
                }
                throw new InvalidOperationException($"The game gave a still-moving {name} return response; no further calibration input was sent. Keep the mouse still and press F8 again.");
            }
            double bodyReturned=world.PlayerHeading();
            AxisSample sample=EvaluateAxisSample(outwardChange,returnChange,restoreError,outwardPixels,returnPixels,outwardSettlement,returned);
            double bodyResponse=vertical?double.NaN:EvaluateBodyResponse(bodyStart,bodyTurned,bodyReturned,outwardPixels,returnPixels);
            sample=sample with{BodyRadiansPerPixel=bodyResponse,Valid=sample.Valid&&(vertical||double.IsFinite(bodyResponse)),
                Reason=sample.Valid&&!vertical&&!double.IsFinite(bodyResponse)?"character-body legs disagreed":sample.Reason};
            trace?.Invoke("3D camera calibration sample",new{Axis=name,Attempt=attempt,OutwardPixels=outwardPixels,ReturnPixels=returnPixels,OutwardChange=outwardChange,
                ReturnChange=returnChange,RestoreError=restoreError,OutwardSettled=outwardSettlement.Settled,ReturnSettled=returned.Settled,
                OutwardDeliveries=outward.Attempts,ReturnDeliveries=returnedLeg.Attempts,
                sample.RadiansPerPixel,sample.Valid,sample.Reason});
            if(sample.Valid)
            {
                accepted.Add(sample);
                for(int i=0;i<accepted.Count-1;i++)
                {
                    double a=accepted[i].RadiansPerPixel,b=sample.RadiansPerPixel;
                    if(!CalibrationSamplesConsistent(a,b)||!vertical&&!CalibrationSamplesConsistent(accepted[i].BodyRadiansPerPixel,sample.BodyRadiansPerPixel))continue;
                    double ratio=Math.Max(Math.Abs(a),Math.Abs(b))/Math.Min(Math.Abs(a),Math.Abs(b));
                    double worstRestore=Math.Max(Math.Abs(accepted[i].RestoreError),Math.Abs(sample.RestoreError));
                    trace?.Invoke("3D camera calibration axis",new{Axis=name,FirstRadiansPerPixel=a,SecondRadiansPerPixel=b,
                        Ratio=ratio,Consistent=true,AcceptedSamples=accepted.Count,WorstRestoreError=worstRestore});
                    double body=vertical?double.NaN:(accepted[i].BodyRadiansPerPixel+sample.BodyRadiansPerPixel)/2;
                    return new((a+b)/2,body,accepted.Count,worstRestore);
                }
            }
            else if(CanRebaseRejectedMeasurement(sample,outwardSettlement,returned,outwardChange,returnChange))
            {
                // A settled but invalid cycle can be a clipped pitch or a transient
                // horizontal measurement. Discard the entire cycle and use its
                // final position as the next cycle's fresh baseline; no invalid
                // sensitivity, or sample from the old camera state, is reused.
                accepted.Clear();
                trace?.Invoke($"3D camera calibration rejected {name} measurement rebased",new{Axis=name,Cycle=attempt,sample.Reason,OutwardChange=outwardChange,
                    ReturnChange=returnChange,RestoreError=restoreError,NextCycle=attempt+1});
                if(attempt<4)status?.Invoke($"Rejected {name} measurement rebased; retrying cycle {attempt+1}/4…");
                continue;
            }
            else if(!CanRetryCalibration(sample))
                throw new InvalidOperationException($"The game did not give a restorable {name} camera response ({sample.Reason}). Keep the mouse still and press F8 again.");
        }
        if(accepted.Count<2)throw new InvalidOperationException($"The game did not give two consistent {name} camera responses. Keep the mouse still and press F8 again.");
        trace?.Invoke("3D camera calibration axis",new{Axis=name,Consistent=false,AcceptedSamples=accepted.Count,
            Samples=accepted.Select(s=>s.RadiansPerPixel).ToArray(),WorstRestoreError=accepted.Max(s=>Math.Abs(s.RestoreError))});
        throw new InvalidOperationException($"The game gave inconsistent {name} camera responses ({string.Join(", ",accepted.Select(s=>s.RadiansPerPixel.ToString("F6")))} radians per pixel). Keep the mouse still and press F8 again.");
    }

    internal static AxisSample EvaluateAxisSample(double outwardChange,double returnChange,double restoreError,int outwardPixels,int returnPixels,CameraSettlement outward,CameraSettlement returned)
    {
        if(!outward.Settled||!returned.Settled)return new(0,double.NaN,restoreError,false,"camera did not settle");
        if(!double.IsFinite(outwardChange)||!double.IsFinite(returnChange)||!double.IsFinite(restoreError)||outwardPixels==0||returnPixels==0)return new(0,double.NaN,restoreError,false,"non-finite measurement");
        double outwardMagnitude=Math.Abs(outwardChange),returnMagnitude=Math.Abs(returnChange);
        if(outwardMagnitude<.025||returnMagnitude<.025)return new(0,double.NaN,restoreError,false,"response was too small or partial");
        double outwardPerPixel=outwardChange/outwardPixels,returnPerPixel=returnChange/returnPixels;
        if(Math.Sign(outwardPerPixel)!=Math.Sign(returnPerPixel))return new(0,double.NaN,restoreError,false,"signed legs disagreed");
        double legRatio=Math.Max(Math.Abs(outwardPerPixel),Math.Abs(returnPerPixel))/Math.Min(Math.Abs(outwardPerPixel),Math.Abs(returnPerPixel));
        if(legRatio>1.5)return new(0,double.NaN,restoreError,false,"outward and return legs disagreed");
        double response=(outwardPerPixel+returnPerPixel)/2;
        if(Math.Abs(response) is <1e-5 or >0.05)return new(0,double.NaN,restoreError,false,"response was outside calibration bounds");
        if(Math.Abs(restoreError)>Math.Max(.02,Math.Max(outwardMagnitude,returnMagnitude)*.35))return new(0,double.NaN,restoreError,false,"camera did not return to its start");
        return new(response,double.NaN,restoreError,true,"accepted");
    }

    internal static double EvaluateBodyResponse(double start,double turned,double returned,int outwardPixels,int returnPixels)
    {
        double a=Wrap(turned-start)/outwardPixels,b=Wrap(returned-turned)/returnPixels;
        // Movement.Face uses the historical body-turn convention: positive
        // heading response to positive mouse input is stored as negative RPP.
        return CalibrationSamplesConsistent(a,b)?-(a+b)/2:double.NaN;
    }

    static Task<LegDelivery> DeliverLeg(World world,Entity owner,string axis,int cycle,string direction,int pixels,double baseline,bool vertical,int maxDeliveries,CancellationToken token,Action<string,object>? trace,Action<string>? status) =>
        RunDeliveredLeg(pixels,baseline,maxDeliveries,
            ()=>CameraAngle(FreshCamera(world,owner),vertical),
            p=>Input.Aim(vertical?0:p,vertical?p:0,token),
            (start,ct)=>WaitForCamera(world,owner,start,vertical,ct),
            (ms,ct)=>Input.Delay(ms,ct),
            (attempt,settlement,delta,reason)=>
            {
                trace?.Invoke("3D camera calibration delivery",new{Axis=axis,Cycle=cycle,Direction=direction,
                    Pixels=pixels,DeliveryAttempt=attempt,AngleDelta=delta,settlement.Observed,settlement.Settled,
                    settlement.ElapsedMs,settlement.QuietMs,settlement.ChangeCount,Reason=reason});
                if(attempt>1||reason.StartsWith("no response",StringComparison.Ordinal))
                    status?.Invoke($"Calibrating {(vertical?"vertical aiming":"horizontal turning")}: {direction} delivery {attempt}/{maxDeliveries}…");
            },token,
            (current,start)=>AngleDifference(current,start,vertical));

    internal static async Task<LegDelivery> RunDeliveredLeg(int pixels,double baseline,int maxDeliveries,
        Func<double> readAngle,Action<int> send,Func<double,CancellationToken,Task<CameraSettlement>> wait,
        Func<int,CancellationToken,Task> delay,Action<int,CameraSettlement,double,string>? report,CancellationToken token,
        Func<double,double,double>? difference=null)
    {
        if(pixels==0||maxDeliveries is <1 or >5)throw new ArgumentOutOfRangeException(nameof(maxDeliveries));
        difference??=(current,start)=>current-start;
        for(int attempt=1;attempt<=maxDeliveries;attempt++)
        {
            token.ThrowIfCancellationRequested();
            double fresh=readAngle(),baselineDrift=difference(fresh,baseline);
            if(!double.IsFinite(fresh)||Math.Abs(baselineDrift)>.001)
            {
                var moved=new CameraSettlement(fresh,false,true,0,0,1);
                report?.Invoke(attempt,moved,baselineDrift,"baseline moved before retry; pulse not resent");
                return new(moved,attempt,false);
            }
            send(pixels);
            CameraSettlement settlement=await wait(baseline,token);
            double delta=difference(settlement.Angle,baseline);
            bool noResponse=!settlement.Observed&&!settlement.Settled&&settlement.ChangeCount==0&&Math.Abs(delta)<=.001;
            report?.Invoke(attempt,settlement,delta,noResponse?attempt<maxDeliveries?"no response; retrying same signed leg":"no response; delivery limit reached":settlement.Settled?"observed and settled":"partial or moving response; not retried");
            if(!noResponse)return new(settlement,attempt,false);
            if(attempt<maxDeliveries)await delay(80,token);
        }
        return new(new CameraSettlement(baseline,false,false,0,0,0),maxDeliveries,true);
    }

    internal static bool CalibrationSamplesConsistent(double first,double second)
    {
        if(!double.IsFinite(first)||!double.IsFinite(second)||Math.Abs(first)<1e-5||Math.Abs(second)<1e-5)return false;
        double ratio=Math.Max(Math.Abs(first),Math.Abs(second))/Math.Min(Math.Abs(first),Math.Abs(second));
        return Math.Sign(first)==Math.Sign(second)&&ratio<=1.35;
    }

    internal static bool CanDiscardQuietPartial(CameraSettlement settlement,double delta) =>
        !settlement.Settled&&double.IsFinite(settlement.Angle)&&double.IsFinite(delta)&&
        settlement.QuietMs>=180&&Math.Abs(delta)<.025;

    internal static bool CanRebaseRejectedMeasurement(AxisSample sample,CameraSettlement outward,CameraSettlement returned,double outwardChange,double returnChange) =>
        !sample.Valid&&outward.Settled&&returned.Settled&&outward.Observed&&returned.Observed&&
        double.IsFinite(outwardChange)&&double.IsFinite(returnChange)&&double.IsFinite(sample.RestoreError);

    static bool CanRetryCalibration(AxisSample sample)=>double.IsFinite(sample.RestoreError)&&Math.Abs(sample.RestoreError)<=.06;
    static double CameraAngle(CameraFrame frame,bool vertical)=>vertical?Pitch(frame.Forward):Yaw(frame.Forward);
    static double AngleDifference(double current,double start,bool vertical)=>vertical?current-start:Wrap(current-start);

    static async Task<CameraSettlement> WaitForCamera(World world,Entity owner,double start,bool vertical,CancellationToken token)
    {
        long began=Environment.TickCount64,deadline=began+1400,lastChange=began;
        bool observed=false;
        int changeCount=0;double previous=start,current=start;
        while(Environment.TickCount64<deadline)
        {
            await Input.Delay(20,token);CameraFrame frame=FreshCamera(world,owner);
            current=CameraAngle(frame,vertical);
            double change=vertical?current-previous:Wrap(current-previous);
            double total=vertical?current-start:Wrap(current-start);
            if(Math.Abs(total)>=.005)observed=true;
            if(Math.Abs(change)>0.00015){previous=current;lastChange=Environment.TickCount64;changeCount++;}
            long now=Environment.TickCount64;
            if(observed&&now-began>=400&&now-lastChange>=180)return new(current,true,true,now-began,now-lastChange,changeCount);
        }
        long ended=Environment.TickCount64;
        return new(current,false,observed,ended-began,ended-lastChange,changeCount);
    }

    static Task<CameraSettlement> WaitForAxisQuiet(World world,Entity owner,bool vertical,CancellationToken token) =>
        WaitForQuiet(
            ()=>CameraAngle(FreshCamera(world,owner),vertical),
            (ms,ct)=>Input.Delay(ms,ct),
            3000,token,
            (current,previous)=>AngleDifference(current,previous,vertical));

    internal static async Task<CameraSettlement> WaitForQuiet(
        Func<double> readAngle,Func<int,CancellationToken,Task> delay,int maxMs,CancellationToken token,
        Func<double,double,double>? difference=null,Func<long>? clock=null)
    {
        if(maxMs<180)throw new ArgumentOutOfRangeException(nameof(maxMs));
        difference??=((current,previous)=>current-previous);
        clock??=(()=>Environment.TickCount64);
        long began=clock(),lastChange=began;double previous=readAngle(),current=previous;int changeCount=0;
        if(!double.IsFinite(previous))return new(previous,false,true,0,0,changeCount);
        while(clock()-began<maxMs)
        {
            token.ThrowIfCancellationRequested();
            await delay(20,token);
            current=readAngle();
            long now=clock();
            if(!double.IsFinite(current))return new(current,false,true,now-began,now-lastChange,changeCount);
            double change=difference(current,previous);
            if(Math.Abs(change)>.00015){previous=current;lastChange=now;changeCount++;}
            if(now-lastChange>=180)return new(current,true,true,now-began,now-lastChange,changeCount);
        }
        long ended=clock();
        return new(current,false,true,ended-began,ended-lastChange,changeCount);
    }

    void RequireCameraCalibration(){if(!CameraCalibrated)throw new InvalidOperationException("3D camera aim is not calibrated. Press F8 again.");}
    static double Yaw(Vector3 f)=>Math.Atan2(f.X,f.Z);
    static double Pitch(Vector3 f)=>Math.Asin(Math.Clamp(Vector3.Normalize(f).Y,-1f,1f));
    static double Wrap(double angle)=>Math.Atan2(Math.Sin(angle),Math.Cos(angle));
    void TraceCamera(string stage,CameraFrame camera,Entity target,CameraError opticalError,CameraError controlError,bool pivotValidated,double pivotRayMiss,double parallaxMultiplier,int x,int y,CameraAimPrecisionResult? precision=null,bool smoothingApplied=false)
    {
        long now=Environment.TickCount64;if(now-lastCameraTrace<250)return;
        Vector3 targetPosition=TargetPoint(target);
        bool sameTarget=previousTraceTargetId==target.Id;
        double seconds=lastCameraTrace==0||!sameTarget?0:(now-lastCameraTrace)/1000.0;
        Vector3 cameraVelocity=seconds>0?(camera.Position-previousTraceCamera)/(float)seconds:Vector3.Zero;
        Vector3 targetVelocity=seconds>0?(targetPosition-previousTraceTarget)/(float)seconds:Vector3.Zero;
        lastCameraTrace=now;previousTraceCamera=camera.Position;previousTraceTarget=targetPosition;previousTraceTargetId=target.Id;
        TraceLog.Record(stage,new{Target=target.Id,
            CameraPosition=new{camera.Position.X,camera.Position.Y,camera.Position.Z},
            CameraForward=new{camera.Forward.X,camera.Forward.Y,camera.Forward.Z},
            CameraVelocity=new{cameraVelocity.X,cameraVelocity.Y,cameraVelocity.Z},
            TargetPosition=new{targetPosition.X,targetPosition.Y,targetPosition.Z},
            TargetVelocity=new{targetVelocity.X,targetVelocity.Y,targetVelocity.Z},
            YawError=opticalError.Yaw,PitchError=opticalError.Pitch,Depth=opticalError.Depth,
            ActualOpticalYawError=opticalError.Yaw,ActualOpticalPitchError=opticalError.Pitch,
            ControlYawError=controlError.Yaw,ControlPitchError=controlError.Pitch,
            ScreenErrorPixels=precision is { } screen && double.IsFinite(screen.ScreenErrorPixels)?(double?)screen.ScreenErrorPixels:null,
            PickRadiusPixels=precision?.PickRadiusPixels,ScreenAligned=precision?.Aligned,
            SmoothingApplied=smoothingApplied,
            PivotValidated=pivotValidated,PivotRayMiss=pivotRayMiss,ParallaxMultiplier=parallaxMultiplier,PixelsX=x,PixelsY=y,
            YawRadiansPerPixel=cameraYawRadiansPerPixel,PitchRadiansPerPixel=cameraPitchRadiansPerPixel});
    }
    readonly record struct CameraError(double Yaw,double Pitch,double Depth);
    internal readonly record struct CameraSettlement(double Angle,bool Settled,bool Observed,long ElapsedMs,long QuietMs,int ChangeCount);
    internal readonly record struct LegDelivery(CameraSettlement Settlement,int Attempts,bool NoResponse);
    internal readonly record struct AxisSample(double RadiansPerPixel,double BodyRadiansPerPixel,double RestoreError,bool Valid,string Reason);
    readonly record struct AxisCalibration(double RadiansPerPixel,double BodyRadiansPerPixel,int AcceptedSamples,double WorstRestoreError);
}
