using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using System.Text;
using TOHE.Roles.Core;
using static TOHE.Options;
using static TOHE.Translator;
using static TOHE.Utils;

namespace TOHE.Roles.Neutral;

internal class Baker : RoleBase
{
    //===========================SETUP================================\\
    private const int Id = 28600;

    public static readonly HashSet<byte> playerIdList = [];
    public static bool HasEnabled => playerIdList.Any();
    public override bool IsDesyncRole => true;
    public override CustomRoles ThisRoleBase => BTOS2Baker.GetBool() ? CustomRoles.Shapeshifter : CustomRoles.Impostor;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.NeutralApocalypse;
    //==================================================================\\

    private static OptionItem BreadNeededToTransform;
    public static OptionItem FamineStarveCooldown;
    private static OptionItem BTOS2Baker;
    private static OptionItem ApocCanSeeReveals;
    private static OptionItem TransformNoMoreBread;
    private static OptionItem RegenBread;
    public static OptionItem CanVent;
    private static byte BreadID = 0;

    public static readonly Dictionary<byte, HashSet<byte>> BreadList = [];
    private static readonly Dictionary<byte, HashSet<byte>> RevealList = [];
    private static readonly Dictionary<byte, HashSet<byte>> BarrierList = [];

    private static bool CanUseAbility;
    public static bool StarvedNonBreaded;

    private enum BreadSync : byte
    {
        AddBread,
        Reveal,
        Barrier,
        RemoveBread,
        SelectBread,
        ClearBarriers,
    }

    public override void SetupCustomOption()
    {
        SetupSingleRoleOptions(Id, TabGroup.NeutralRoles, CustomRoles.Baker, 1, zeroOne: false);
        BreadNeededToTransform = IntegerOptionItem.Create(Id + 10, "BakerBreadNeededToTransform", new(1, 5, 1), 3, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Baker])
            .SetValueFormat(OptionFormat.Times);
        FamineStarveCooldown = FloatOptionItem.Create(Id + 11, "FamineStarveCooldown", new(0f, 180f, 2.5f), 30f, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Baker])
                .SetValueFormat(OptionFormat.Seconds);
        BTOS2Baker = BooleanOptionItem.Create(Id + 12, "BakerBreadGivesEffects", true, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Baker]);
        ApocCanSeeReveals = BooleanOptionItem.Create(Id + 16, "BakerApocCanSeeReveals", true, TabGroup.NeutralRoles, false).SetParent(BTOS2Baker);
        TransformNoMoreBread = BooleanOptionItem.Create(Id + 13, "BakerTransformNoMoreBread", true, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Baker]);
        CanVent = BooleanOptionItem.Create(Id + 14, "BakerCanVent", true, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Baker]);
        RegenBread = BooleanOptionItem.Create(Id + 15, "BakerRegenBread", true, TabGroup.NeutralRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Baker]);
    }
    public override void Init()
    {
        playerIdList.Clear();
        BreadList.Clear();
        RevealList.Clear();
        BarrierList.Clear();
        Famine.FamineList.Clear();
        CanUseAbility = false;
        StarvedNonBreaded = false;
    }
    public override void Add(byte playerId)
    {
        playerIdList.Add(playerId);
        BreadList[playerId] = [];
        RevealList[playerId] = [];
        BarrierList[playerId] = [];
        CanUseAbility = true;
        StarvedNonBreaded = false;
        BreadID = 0;
        CustomRoleManager.CheckDeadBodyOthers.Add(OnPlayerDead);
    }
    public override void Remove(byte playerId) => CustomRoleManager.CheckDeadBodyOthers.Remove(OnPlayerDead);

    private static (int Breaded, int Required) BreadedPlayerCount(byte playerId)
    {
        int breaded = 0, all = BreadNeededToTransform.GetInt();
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (pc.PlayerId == playerId) continue;

            if (HasBread(playerId, pc.PlayerId))
                breaded++;
        }
        return (breaded, all);
    }
    public static byte CurrentBread() => BreadID;
    private static void SendRPC(BreadSync update, PlayerControl baker, byte value)
    {
        bool shareWithApocalypse = update is BreadSync.AddBread or BreadSync.RemoveBread
            || (update == BreadSync.Reveal && ApocCanSeeReveals.GetBool());
        foreach (var seer in Main.AllPlayerControls)
        {
            if (!seer.IsNonHostModdedClient() || (seer.PlayerId != baker.PlayerId && !(shareWithApocalypse && seer.IsNeutralApocalypse()))) continue;

            CustomRpcTransport.Send(CustomRPC.SyncRoleSkill, writer =>
            {
                writer.WriteNetObject(baker);
                writer.Write((byte)update);
                writer.Write(baker.PlayerId);
                writer.Write(value);
            }, seer.GetClientId());
        }
    }
    public override void ReceiveRPC(MessageReader reader, PlayerControl baker)
    {
        var update = (BreadSync)reader.ReadByte();
        byte bakerId = reader.ReadByte();
        byte value = reader.ReadByte();

        switch (update)
        {
            case BreadSync.AddBread:
                BreadList[bakerId].Add(value);
                break;
            case BreadSync.Reveal:
                RevealList[bakerId].Add(value);
                break;
            case BreadSync.Barrier:
                BarrierList[bakerId].Add(value);
                break;
            case BreadSync.RemoveBread:
                BreadList[bakerId].Remove(value);
                break;
            case BreadSync.SelectBread:
                BreadID = value;
                break;
            case BreadSync.ClearBarriers:
                BarrierList[bakerId].Clear();
                break;
        }
    }
    public override string GetProgressText(byte playerId, bool comms)
    {
        var (breaded, required) = BreadedPlayerCount(playerId);
        return ColorString(GetRoleColor(CustomRoles.Baker).ShadeColor(0.25f), $"({breaded}/{required})");
    }
    public override bool OthersKnowTargetRoleColor(PlayerControl seer, PlayerControl target) => KnowRoleTarget(seer, target);
    public override bool KnowRoleTarget(PlayerControl seer, PlayerControl target)
    {
        if (target.IsNeutralApocalypse() && seer.IsNeutralApocalypse()) return true;
        // i swear this isn't consigliere's code i swear
        if (seer.IsAlive() && RevealList.TryGetValue(seer.PlayerId, out var targets))
        {
            return targets.Contains(target.PlayerId);
        }
        return false;
    }
    public static bool ApocKnowRoleTarget(PlayerControl apoc, PlayerControl target)
        => apoc != null && apoc.IsNeutralApocalypse() && ApocCanSeeReveals.GetBool()
            && RevealList.Values.Any(targets => targets.Contains(target.PlayerId));
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        StringBuilder sb = new();
        if (BarrierList[seer.PlayerId].Contains(seen.PlayerId))
        {
            sb.Append(ColorString(GetRoleColor(CustomRoles.Baker), "●") + ColorString(GetRoleColor(CustomRoles.Medic), "✚"));
            return sb.ToString();
        }
        else if (HasBread(seer.PlayerId, seen.PlayerId))
        {
            sb.Append(ColorString(GetRoleColor(CustomRoles.Baker), "●"));
            return sb.ToString();
        }
        return string.Empty;
    }
    public override string GetMarkOthers(PlayerControl seer, PlayerControl target, bool isForMeeting = false)
    {
        if (playerIdList.Any() && HasBread(playerIdList.First(), target.PlayerId) && seer.IsNeutralApocalypse() && seer.PlayerId != playerIdList.First())
        {
            return ColorString(GetRoleColor(CustomRoles.Baker), "●");
        }
        return string.Empty;
    }
    public override bool CanUseKillButton(PlayerControl pc) => pc.IsAlive();
    public override bool CanUseImpostorVentButton(PlayerControl pc) => CanVent.GetBool();
    public override void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = Main.AllPlayerKillCooldown[id];
    public override void SetAbilityButtonText(HudManager hud, byte playerId)
    {
        hud.KillButton.OverrideText(GetString("BakerKillButtonText"));
        if (BTOS2Baker.GetBool()) hud.AbilityButton.OverrideText(GetString("BakerUnshiftButtonText"));
    }
    public static bool HasBread(byte pc, byte target) => BreadList.TryGetValue(pc, out var breadList) && breadList.Contains(target);

    public override void OnReportDeadBody(PlayerControl marg, NetworkedPlayerInfo iscute)
    {
        CanUseAbility = true;
    }
    private void OnPlayerDead(PlayerControl killer, PlayerControl deadPlayer, bool inMeeting)
    {
        var baker = _Player;
        if (baker == null || !baker.Is(CustomRoles.Baker) || !BreadList[baker.PlayerId].Remove(deadPlayer.PlayerId)) return;

        Logger.Info($"{deadPlayer.GetNameWithRole()} died, remove them from BreadList", "Baker");
        SendRPC(BreadSync.RemoveBread, baker, deadPlayer.PlayerId);
        if (RegenBread.GetBool() && !CanUseAbility && baker.IsAlive())
        {
            CanUseAbility = true;
            baker.Notify(string.Format(GetString("BakerBreadDied"), deadPlayer.GetRealName()));
        }
    }
    public override void UnShapeShiftButton(PlayerControl pc)
    {
        if (BTOS2Baker.GetBool())
        {
            var sb = new StringBuilder();
            switch (BreadID) // 0 = Reveal, 1 = Roleblock, 2 = Barrier
            {
                case 0: // Switch to Roleblock
                    BreadID = 1;
                    sb.Append(GetString("BakerSwitchBread") + GetString("BakerRoleblockBread"));
                    break;
                case 1: // Switch to Barrier
                    BreadID = 2;
                    sb.Append(GetString("BakerSwitchBread") + GetString("BakerBarrierBread"));
                    break;
                case 2: // Switch to Reveal
                    BreadID = 0;
                    sb.Append(GetString("BakerSwitchBread") + GetString("BakerRevealBread"));
                    break;
            }
            SendRPC(BreadSync.SelectBread, pc, BreadID);
            pc.Notify(sb.ToString());
        }
    }
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        if (seer == null || !seer.IsAlive() || isForMeeting || !isForHud) return string.Empty;
        if (!BTOS2Baker.GetBool()) return string.Empty;
        else
        {
            var sb = new StringBuilder();
            switch (BreadID)
            {
                case 0: // Reveal
                    sb.Append(GetString("BakerCurrentBread") + GetString("BakerRevealBread"));
                    break;
                case 1: // Roleblock
                    sb.Append(GetString("BakerCurrentBread") + GetString("BakerRoleblockBread"));
                    break;
                case 2: // Barrier
                    sb.Append(GetString("BakerCurrentBread") + GetString("BakerBarrierBread"));
                    break;
            }
            return sb.ToString();
        }
    }
    public override bool ForcedCheckMurderAsKiller(PlayerControl killer, PlayerControl target)
    {
        if (!CanUseAbility)
            killer.Notify(GetString("BakerBreadUsedAlready"));

        else if (target.IsNeutralApocalypse())
            killer.Notify(GetString("BakerCantBreadApoc"));

        else if (HasBread(killer.PlayerId, target.PlayerId))
            killer.Notify(GetString("BakerAlreadyBreaded"));

        else
        {
            BreadList[killer.PlayerId].Add(target.PlayerId);
            SendRPC(BreadSync.AddBread, killer, target.PlayerId);
            CanUseAbility = false;

            Logger.Info($"Bread given to " + target.GetRealName(), "Baker");

            if (BTOS2Baker.GetBool())
            {
                switch (BreadID)
                {
                    case 0: // Reveal
                        RevealList[killer.PlayerId].Add(target.PlayerId);
                        SendRPC(BreadSync.Reveal, killer, target.PlayerId);
                        break;
                    case 1: // Roleblock
                        target.SetKillCooldownV3(999f);
                        break;
                    case 2: // Barrier
                        BarrierList[killer.PlayerId].Add(target.PlayerId);
                        SendRPC(BreadSync.Barrier, killer, target.PlayerId);
                        break;
                }
            }

            foreach (var seer in Main.AllAlivePlayerControls)
            {
                if (seer.IsNeutralApocalypse() && !seer.IsModded())
                    NotifyRoles(SpecifySeer: seer, SpecifyTarget: target);
            }
            killer.Notify(GetString("BakerBreaded"));
        }
        return false;
    }
    public override bool CheckMurderOnOthersTarget(PlayerControl killer, PlayerControl target)
    {
        if (_Player == null || !_Player.IsAlive()) return false;
        if (!BarrierList[_Player.PlayerId].Contains(target.PlayerId)) return false;

        killer.RpcGuardAndKill(target);
        killer.ResetKillCooldown();
        killer.SetKillCooldown();

        NotifyRoles(SpecifySeer: killer, SpecifyTarget: target, ForceLoop: true);
        NotifyRoles(SpecifySeer: target, SpecifyTarget: killer, ForceLoop: true);
        return true;
    }
    public override void AfterMeetingTasks()
    {
        var baker = _Player;
        if (baker == null || BarrierList[baker.PlayerId].Count == 0) return;

        BarrierList[baker.PlayerId].Clear();
        SendRPC(BreadSync.ClearBarriers, baker, 0);
    }
    public override void OnFixedUpdate(PlayerControl player, bool lowLoad, long nowTime)
    {
        if (lowLoad || !player.IsAlive()) return;

        var (breaded, required) = BreadedPlayerCount(player.PlayerId);
        if (breaded < required && (!TransformNoMoreBread.GetBool() || Main.AllAlivePlayerControls.Any(
            pc => !pc.IsNeutralApocalypse() && !HasBread(player.PlayerId, pc.PlayerId)))) return;

        Logger.Info($"{player.GetRealName()} transformed to Famine with {breaded}/{required} bread", "Baker");
        OnRemove(player.PlayerId);
        if (Main.UnShapeShifter.Remove(player.PlayerId) && player.CurrentOutfitType == PlayerOutfitType.Shapeshifted)
            player.RpcShapeshift(player, false);

        player.RpcChangeRoleBasis(CustomRoles.Famine);
        player.RpcSetCustomRole(CustomRoles.Famine);
        player.GetRoleClass()?.OnAdd(player.PlayerId);
        player.ResetKillCooldown();
        player.SyncSettings();
        player.Notify(GetString("BakerToFamine"));
        player.RpcGuardAndKill(player);
    }
}
internal class Famine : RoleBase
{
    //===========================SETUP================================\\
    public static bool HasEnabled => CustomRoleManager.HasEnabled(CustomRoles.Famine);
    public override bool IsDesyncRole => true;
    public override CustomRoles ThisRoleBase => CustomRoles.Impostor;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.NeutralApocalypse;
    //==================================================================\\

    public static readonly Dictionary<byte, HashSet<byte>> FamineList = [];

    public override void Add(byte playerId) => FamineList[playerId] = [];
    public override bool OthersKnowTargetRoleColor(PlayerControl seer, PlayerControl target) => KnowRoleTarget(seer, target);
    public override bool KnowRoleTarget(PlayerControl seer, PlayerControl target) => CustomRoles.Baker.GetStaticRoleClass().KnowRoleTarget(seer, target);
    public override void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = Baker.FamineStarveCooldown.GetFloat();
    public override void ApplyGameOptions(IGameOptions opt, byte playerId) => opt.SetVision(true);
    public override bool CanUseKillButton(PlayerControl pc) => true;
    public override bool CanUseImpostorVentButton(PlayerControl pc) => Baker.CanVent.GetBool();

    public override bool OnCheckMurderAsTarget(PlayerControl killer, PlayerControl target) => false;
    public override void SetAbilityButtonText(HudManager hud, byte playerId) => hud.KillButton.OverrideText(GetString("FamineKillButtonText"));
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
        => FamineList[seer.PlayerId].Contains(seen.PlayerId) ? $"<color={GetRoleColorCode(seer.GetCustomRole())}>⁂</color>" : string.Empty;

    private static void SendRPC(PlayerControl player, byte targetId)
    {
        if (!player.IsNonHostModdedClient()) return;
        CustomRpcTransport.Send(CustomRPC.SyncRoleSkill, writer =>
        {
            writer.WriteNetObject(player);
            writer.Write(player.PlayerId);
            writer.Write(targetId);
        }, player.GetClientId());
    }
    public override void ReceiveRPC(MessageReader reader, PlayerControl famine)
    {
        byte famineId = reader.ReadByte();
        byte targetId = reader.ReadByte();

        if (targetId == byte.MaxValue) FamineList[famineId].Clear();
        else FamineList[famineId].Add(targetId);
    }

    public override bool ForcedCheckMurderAsKiller(PlayerControl killer, PlayerControl target)
    {
        if (target.IsNeutralApocalypse())
            killer.Notify(GetString("FamineCantStarveApoc"));

        else if (FamineList[killer.PlayerId].Contains(target.PlayerId))
            killer.Notify(GetString("FamineAlreadyStarved"));

        else if (Baker.StarvedNonBreaded)
        {
            FamineList[killer.PlayerId].Add(target.PlayerId);
            SendRPC(killer, target.PlayerId);
            NotifyRoles(SpecifySeer: killer);
            killer.Notify(GetString("FamineStarved"));
            Logger.Info(target.GetRealName() + $" has been starved", "Famine");
        }
        return false;
    }
    public override void OnReportDeadBody(PlayerControl sylveon, NetworkedPlayerInfo iscute)
    {
        var famine = _Player;
        if (famine == null) return;
        var starved = FamineList[famine.PlayerId];
        if (starved.Count == 0) return;

        foreach (var targetId in starved)
        {
            var target = targetId.GetPlayer();
            if (target == null || !target.IsAlive()) continue;
            target.RpcExileV2();
            target.SetRealKiller(famine);
            targetId.SetDeathReason(PlayerState.DeathReason.Starved);
            Main.PlayerStates[targetId].SetDead();
            MurderPlayerPatch.AfterPlayerDeathTasks(famine, target, true);
            Logger.Info($"{famine.GetRealName()} has starved {target.GetRealName()}", "Famine");
        }

        starved.Clear();
        SendRPC(famine, byte.MaxValue);
    }
    public override void OnCheckForEndVoting(PlayerState.DeathReason deathReason, params byte[] exileIds)
    {
        Logger.Info("Starting OnCheckForEndVoting", "Famine");
        var famine = _Player;
        if (Baker.StarvedNonBreaded || famine == null) return;

        Logger.Info("Checking if Baker alive", "Famine");
        if (!famine.IsAlive())
        {
            Logger.Info("Baker dead", "Famine");
            return;
        }
        Logger.Info("Baker Alive", "Famine");
        if (exileIds.Contains(famine.PlayerId) || Main.AfterMeetingDeathPlayers.ContainsKey(famine.PlayerId)) return;

        var deathList = new List<byte>();
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (pc.IsNeutralApocalypse() || Baker.HasBread(famine.PlayerId, pc.PlayerId)
                || Main.AfterMeetingDeathPlayers.ContainsKey(pc.PlayerId)) continue;

            Logger.Info($"{famine.GetRealName()} starving {pc.GetRealName()} because they had no bread", "Famine");
            pc.SetRealKiller(famine);
            deathList.Add(pc.PlayerId);
        }
        CheckForEndVotingPatch.TryAddAfterMeetingDeathPlayers(PlayerState.DeathReason.Starved, [.. deathList]);
        if (Baker.BreadList.TryGetValue(famine.PlayerId, out var bread)) bread.Clear();
        Baker.StarvedNonBreaded = true;
        Logger.Info("Ended OnCheckForEndVoting", "Famine");
    }
}
