namespace PoteHunter;

public readonly record struct TravelKeys(bool Forward,bool Back,bool Left,bool Right,Vec Direction);

public sealed partial class Movement
{
    bool traveling;
    Vec travelGoal,travelProgressPosition;
    bool travelForward,travelBack,travelLeft,travelRight;
    long travelProgressAt;
    public bool IsTraveling => traveling;

    public static TravelKeys CalculateTravelKeys(Vec position,Vec waypoint,double playerHeading)
    {
        if(!position.Finite || !waypoint.Finite || !double.IsFinite(playerHeading))
            throw new ArgumentOutOfRangeException(nameof(waypoint));
        Vec delta=waypoint-position;
        if(delta.Length<.001)return new(false,false,false,false,default);
        Vec forward=FromClientHeading(playerHeading);
        // Ranged travel never backs away or strafes. It keeps W held while the
        // mouse turns toward the waypoint, rather than pressing S, A, or D.
        if(forward.Length>0)forward/=forward.Length;
        return new(true,false,false,false,forward);
    }

    public bool StopTravel()
    {
        bool wasTraveling=traveling;
        Input.Hold(Keys.W,false,default);Input.Hold(Keys.S,false,default);
        Input.Hold(Keys.A,false,default);Input.Hold(Keys.D,false,default);
        traveling=false;
        travelForward=travelBack=travelLeft=travelRight=false;
        return wasTraveling;
    }

    // Called by the input preflight before every new held input. It prevents a
    // key already selected for travel from crossing a boundary as observations
    // change between movement ticks.
    public bool ValidateTravelStep(World world)
    {
        if(!traveling)return true;
        Vec position=world.PlayerPosition();
        double remaining=(travelGoal-position).Length;
        double step=Math.Clamp(remaining,.35,1.0);
        Vec forward=FromClientHeading(world.PlayerHeading());
        Vec right=new(forward.Y,-forward.X);
        Vec currentDirection=forward*(travelForward?1:travelBack?-1:0)+right*(travelRight?1:travelLeft?-1:0);
        if(currentDirection.Length>0)currentDirection/=currentDirection.Length;
        if(remaining<=.65 || !position.Finite || currentDirection.Length<.9 ||
            CanAdvance?.Invoke(position,position+currentDirection*step)==false)
        {
            StopTravel();return false;
        }
        return true;
    }

    public async Task<bool> TravelToward(World world,Vec waypoint,Entity aimTarget,CancellationToken token)
    {
        Vec position=world.PlayerPosition();
        if((waypoint-position).Length<=.65){StopTravel();return true;}
        // Establish a forward-facing travel vector before deciding which held
        // movement keys are safe. This prevents the old backward correction.
        bool aimed=await FaceTarget3D(world,aimTarget,token,.03);
        TravelKeys keys=CalculateTravelKeys(position,waypoint,world.PlayerHeading());
        double headingError=Math.Abs(Angle(FromClientHeading(world.PlayerHeading()),waypoint-position));
        // Keep W held through fine mouse steering, but do not draw circles when
        // the character is still facing far away from the travel waypoint.
        if(headingError>Math.PI/4)keys=keys with {Forward=false,Direction=default};
        if(keys.Direction.Length<.9 || CanAdvance?.Invoke(position,position+keys.Direction*Math.Clamp((waypoint-position).Length,.35,1.0))==false)
        {
            StopTravel();return false;
        }
        long now=Environment.TickCount64;
        if(!traveling || (waypoint-travelGoal).Length>.1)
        {
            travelProgressPosition=position;travelProgressAt=now;
        }
        travelGoal=waypoint;
        travelForward=keys.Forward;travelBack=keys.Back;travelLeft=keys.Left;travelRight=keys.Right;traveling=true;
        // Input rechecks admission after ProtectionPreflight. If that preflight
        // stopped travel, a stale caller cannot press a movement key again.
        Input.Hold(Keys.W,keys.Forward,token,()=>traveling);Input.Hold(Keys.S,keys.Back,token,()=>traveling);
        Input.Hold(Keys.A,false,token,()=>traveling);Input.Hold(Keys.D,false,token,()=>traveling);
        if((position-travelProgressPosition).Length>.15){travelProgressPosition=position;travelProgressAt=now;}
        else if(now-travelProgressAt>1800){StopTravel();throw new MovementBlockedException(position,keys.Direction);}
        TraceLog.Record("ranged travel feedback",new{Position=position,Waypoint=waypoint,keys.Forward,keys.Back,keys.Left,keys.Right,Aimed=aimed,HeadingErrorDegrees=headingError*180/Math.PI});
        return false;
    }
}
