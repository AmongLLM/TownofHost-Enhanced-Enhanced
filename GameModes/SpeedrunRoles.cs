using AmongUs.GameOptions;
using System;
using TOHE.Roles.Core;
using UnityEngine;
using static TOHE.SpeedrunManager;
using static TOHE.Translator;
using static TOHE.Utils;

namespace TOHE;

public sealed class Runner : RoleBase
{
    public override CustomRoles ThisRoleBase => CustomRoles.Crewmate;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.None;

    private float remainingProtection;
    private float remainingSpeedBoost;

    public override void Add(byte playerId)
    {
        Progress.TryAdd(playerId, default);
    }

    public override bool HasTasks(NetworkedPlayerInfo player, CustomRoles role, bool forRecompute)
        => !Progress.GetValueOrDefault(player.PlayerId).HasFinished;

    public override bool CanUseKillButton(PlayerControl pc)
        => !IsTaskRace && pc.IsAlive() && Progress.GetValueOrDefault(pc.PlayerId).HasFinished;

    public override bool CanUseSabotage(PlayerControl pc) => Speedrun_AllowCloseDoor.GetBool() && CanUseKillButton(pc);

    public override bool CanUseImpostorVentButton(PlayerControl pc) => false;

    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        opt.SetVision(true);
        float speed = Progress[playerId].HasFinished ? Speedrun_RunnerSpeedAfterFinishTasks.GetFloat()
            : remainingSpeedBoost > 0f ? Speedrun_SpeedBoostSpeed.GetFloat() : Speedrun_RunnerNormalSpeed.GetFloat();
        Main.AllPlayerSpeed[playerId] = speed;
        AURoleOptions.PlayerSpeedMod = speed;
    }

    public override void SetKillCooldown(byte id)
    {
        int eliminations = Progress.Values.Count(progress => progress.IsEliminated);
        Main.AllPlayerKillCooldown[id] = Math.Max(0.5f,
            Speedrun_RunnerKcd.GetFloat() - Speedrun_RunnerKcdPerDeadPlayer.GetFloat() * eliminations);
    }

    public override bool OnTaskComplete(PlayerControl player, int completedTaskCount, int totalTaskCount)
    {
        if (!player.IsAlive() || Progress[player.PlayerId].HasFinished) return true;

        var progress = Progress[player.PlayerId];
        if (Speedrun_SpeedBoostAfterTask.GetBool())
        {
            remainingSpeedBoost = Speedrun_SpeedBoostDuration.GetFloat();
            player.MarkDirtySettings();
        }
        if (!IsTaskRace && Speedrun_ProtectAfterTask.GetBool())
        {
            remainingProtection = Speedrun_ProtectDuration.GetFloat();
            progress = progress with { IsProtected = true };
        }

        Progress[player.PlayerId] = progress with { CompletedTasks = completedTaskCount, TotalTasks = totalTaskCount };
        if (completedTaskCount >= totalTaskCount) FinishTasks(player);
        ResetPersonalTimer(player.PlayerId, GetTimeStamp());
        SyncProgress(player.PlayerId);
        NotifyRoles(SpecifyTarget: player, ForceLoop: true);
        return true;
    }

    internal void FinishTasks(PlayerControl player)
    {
        byte id = player.PlayerId;
        Progress[id] = Progress[id] with
        {
            FinishSeconds = StartedAt == 0 ? 0 : (int)(GetTimeStamp() - StartedAt),
            FinishPlace = (byte)(Progress.Values.Count(progress => progress.HasFinished) + 1)
        };
        remainingSpeedBoost = 0f;

        if (!IsTaskRace)
        {
            // Record the runner's self view so role restoration preserves it.
            RpcSetRoleReplacer.RoleMap[(id, id)] = (RoleTypes.Impostor, CustomRoles.Runner);
            player.RpcSetRoleDesync(RoleTypes.Impostor, player.OwnerId);
            player.ResetKillCooldown();
            player.SetKillCooldown();
        }
        player.MarkDirtySettings();
    }

    public override bool OnCheckMurderAsKiller(PlayerControl killer, PlayerControl target)
    {
        if (!CanUseKillButton(killer) || target.GetRoleClass() is not Runner runner) return false;

        var progress = Progress[target.PlayerId];
        if (!progress.IsProtected) return true;

        if (Speedrun_ProtectOnlyOnce.GetBool())
        {
            runner.remainingProtection = 0f;
            Progress[target.PlayerId] = progress with { IsProtected = false };
            SyncProgress(target.PlayerId);
            NotifyRoles(SpecifyTarget: target, ForceLoop: true);
        }
        killer.SetKillCooldown(Speedrun_ProtectKcd.GetFloat(), target, forceAnime: true);
        target.RpcGuardAndKill(target, fromSetKCD: true);
        return false;
    }

    public override void OnMurderPlayerAsTarget(PlayerControl killer, PlayerControl target, bool inMeeting, bool isSuicide)
    {
        // The disconnect callback arrives before the player's dead state is set.
        if (!target.IsDisconnected()) OnElimination(target.PlayerId, killer.PlayerId);
    }

    public override void OnFixedUpdate(PlayerControl player, bool lowLoad, long nowTime)
    {
        if (!player.IsAlive()) return;

        if (remainingSpeedBoost > 0f)
        {
            remainingSpeedBoost -= Time.fixedDeltaTime;
            if (remainingSpeedBoost <= 0f) player.MarkDirtySettings();
        }
        if (remainingProtection <= 0f) return;

        remainingProtection -= Time.fixedDeltaTime;
        if (remainingProtection <= 0f)
        {
            byte id = player.PlayerId;
            Progress[id] = Progress[id] with { IsProtected = false };
            SyncProgress(id);
            NotifyRoles(SpecifyTarget: player, ForceLoop: true);
        }
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen, bool isForMeeting = false)
        => seer == seen && !isForMeeting && Progress.GetValueOrDefault(seer.PlayerId).IsProtected
            ? ColorString(GetRoleColor(CustomRoles.Medic), "✚") : string.Empty;

    public override string GetMarkOthers(PlayerControl seer, PlayerControl seen, bool isForMeeting = false)
        => seer != seen && !isForMeeting && seen != null && Progress.GetValueOrDefault(seen.PlayerId).IsProtected
            ? ColorString(GetRoleColor(CustomRoles.Medic), "✚") : string.Empty;

    public override string GetSuffix(PlayerControl seer, PlayerControl target, bool isForMeeting = false)
    {
        if (isForMeeting || !seer.IsAlive() || (target != null && seer != target)) return string.Empty;

        string arrows = TargetArrow.GetAllArrows(seer);
        long deadline = Progress.GetValueOrDefault(seer.PlayerId).PersonalDeadline;
        if (deadline == 0) return arrows;

        string timer = string.Format(GetString("Speedrun_PersonalTimeRemaining"), Math.Max(0, deadline - GetTimeStamp()));
        return arrows.Length == 0 ? timer : $"{arrows}\n{timer}";
    }

    public override string GetProgressText(byte playerId, bool comms)
        => SpeedrunManager.GetProgressText(playerId, PlayerControl.LocalPlayer);

    public override bool KnowRoleTarget(PlayerControl seer, PlayerControl target) => target.Is(CustomRoles.Runner);
}
