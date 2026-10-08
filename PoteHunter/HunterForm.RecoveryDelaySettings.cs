namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox delayRouteReturn=new(){Name="delayRouteReturn",Text="Delay route return",AutoSize=true};
    readonly NumericUpDown returnDelaySeconds=new(){Name="returnDelaySeconds",Minimum=0,Maximum=600,Value=10,Width=65,AccessibleName="Route return delay in seconds"};

    void ApplyRecoveryReturnDelaySettings(Options options)
    {
        delayRouteReturn.Checked=options.DelayReturnAfterRevival;
        returnDelaySeconds.Value=Math.Clamp(options.ReturnDelaySeconds,0,600);
        RefreshRecoveryReturnDelaySettings();
    }

    void RefreshRecoveryReturnDelaySettings()
    {
        bool editable=!working&&!busy&&!clientRecoveryRunning&&!navigation.Recording&&autoRevive.Checked&&!healerMode.Checked;
        delayRouteReturn.Enabled=editable;
        returnDelaySeconds.Enabled=editable&&delayRouteReturn.Checked;
    }

    void QueueRecoveryReturnDelaySave()
    {
        if(working||busy||clientRecoveryRunning||navigation.Recording)return;
        QueueCompactSave();
    }

    void AddRecoveryReturnDelaySettings(TableLayoutPanel card)
    {
        var row=CompactRow("Before return",delayRouteReturn,returnDelaySeconds,Caption("s"));
        row.Name="recoveryReturnDelayRow";CompactAdd(card,row);
        priorityHint.SetToolTip(delayRouteReturn,"After living HP is confirmed and any automatic repair finishes, wait before following the saved return route. Off keeps the current immediate return. This does not delay revival or repair.");
        priorityHint.SetToolTip(returnDelaySeconds,"Wait 0–600 seconds before route return when Delay route return is enabled. The saved value is retained when the toggle is off.");
        delayRouteReturn.CheckedChanged+=(_,_)=>{RefreshRecoveryReturnDelaySettings();QueueRecoveryReturnDelaySave();};
        returnDelaySeconds.ValueChanged+=(_,_)=>QueueRecoveryReturnDelaySave();
        autoRevive.CheckedChanged+=(_,_)=>RefreshRecoveryReturnDelaySettings();
        healerMode.CheckedChanged+=(_,_)=>RefreshRecoveryReturnDelaySettings();
        settings.EnabledChanged+=(_,_)=>RefreshRecoveryReturnDelaySettings();
        timer.Tick+=(_,_)=>RefreshRecoveryReturnDelaySettings();
        RefreshRecoveryReturnDelaySettings();
    }
}
