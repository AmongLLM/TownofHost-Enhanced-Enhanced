using Hazel;
using System;
using System.Text;
using TOHE.Roles.Core;
using UnityEngine;
using static TOHE.Translator;
using static TOHE.Utils;

namespace TOHE;

public static class SpeedrunManager
{
    private const int Id = 67_225_001;

    public static OptionItem Speedrun_NumCommonTasks;
    public static OptionItem Speedrun_NumShortTasks;
    public static OptionItem Speedrun_NumLongTasks;
    public static OptionItem Speedrun_AliveCanSeeOtherTaskCounts;

    public static OptionItem Speedrun_ShowChatInGame;

    public static OptionItem Speedrun_RunnerNormalSpeed;
    public static OptionItem Speedrun_RunnerKcd;
    public static OptionItem Speedrun_RunnerKcdPerDeadPlayer;
    public static OptionItem Speedrun_RunnerSpeedAfterFinishTasks;
    public static OptionItem Speedrun_AllowCloseDoor;

    public static OptionItem Speedrun_ArrowPlayers;
    public static OptionItem Speedrun_ArrowPlayersPlayerLiving;

    public static OptionItem Speedrun_SpeedBoostAfterTask;
    public static OptionItem Speedrun_SpeedBoostSpeed;
    public static OptionItem Speedrun_SpeedBoostDuration;

    public static OptionItem Speedrun_ProtectAfterTask;
    public static OptionItem Speedrun_ProtectDuration;
    public static OptionItem Speedrun_ProtectOnlyOnce;
    public static OptionItem Speedrun_ProtectKcd;

    public static OptionItem Speedrun_EndGameForTime;
    public static OptionItem Speedrun_MaxTimeForTie;
    public static OptionItem Speedrun_EnablePersonalTimer;
    public static OptionItem Speedrun_PersonalTimeLimit;
    public static OptionItem Speedrun_TaskFinishWins;
    public static OptionItem Speedrun_TaskWinnerCount;

    public static void SetupCustomOption()
    {
        TextOptionItem.Create(10000029, "MenuTitle.Speedrun", TabGroup.ModSettings)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor);

        Speedrun_NumCommonTasks = IntegerOptionItem.Create(Id + 1, "Speedrun_NumCommonTasks", new(0, 10, 1), 1, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetHeader(true);
        Speedrun_NumShortTasks = IntegerOptionItem.Create(Id + 2, "Speedrun_NumShortTasks", new(0, 15, 1), 1, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor);
        Speedrun_NumLongTasks = IntegerOptionItem.Create(Id + 3, "Speedrun_NumLongTasks", new(0, 15, 1), 1, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor);
        Speedrun_AliveCanSeeOtherTaskCounts = BooleanOptionItem.Create(Id + 25, "Speedrun_AliveCanSeeOtherTaskCounts", true, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor);

        Speedrun_ShowChatInGame = BooleanOptionItem.Create(Id + 20, "Speedrun_ShowChatInGame", false, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor);

        Speedrun_RunnerNormalSpeed = FloatOptionItem.Create(Id + 4, "Speedrun_RunnerNormalSpeed", new(0.25f, 5f, 0.25f), 1.5f, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetValueFormat(OptionFormat.Multiplier)
            .SetColor(ModeColor)
            .SetHeader(true);
        Speedrun_RunnerKcd = FloatOptionItem.Create(Id + 5, "Speedrun_RunnerKcd", new(0.5f, 120f, 0.5f), 15f, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetValueFormat(OptionFormat.Seconds);
        Speedrun_RunnerKcdPerDeadPlayer = FloatOptionItem.Create(Id + 6, "Speedrun_RunnerKcdPerDeadPlayer", new(0f, 60f, 0.05f), 0f, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetValueFormat(OptionFormat.Seconds);
        Speedrun_RunnerSpeedAfterFinishTasks = FloatOptionItem.Create(Id + 7, "Speedrun_RunnerSpeedAfterFinishTasks", new(0.25f, 5f, 0.25f), 2.5f, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetValueFormat(OptionFormat.Multiplier);
        Speedrun_AllowCloseDoor = BooleanOptionItem.Create(Id + 8, "Speedrun_AllowCloseDoor", false, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor);

        Speedrun_ArrowPlayers = BooleanOptionItem.Create(Id + 9, "Speedrun_ArrowPlayers", true, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetHeader(true);
        Speedrun_ArrowPlayersPlayerLiving = IntegerOptionItem.Create(Id + 10, "Speedrun_ArrowPlayersPlayerLiving", new(2, 127, 1), 2, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetParent(Speedrun_ArrowPlayers)
            .SetColor(ModeColor);

        Speedrun_SpeedBoostAfterTask = BooleanOptionItem.Create(Id + 11, "Speedrun_SpeedBoostAfterTask", true, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetHeader(true);
        Speedrun_SpeedBoostSpeed = FloatOptionItem.Create(Id + 12, "Speedrun_SpeedBoostSpeed", new(0.25f, 5f, 0.25f), 2.5f, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetParent(Speedrun_SpeedBoostAfterTask)
            .SetValueFormat(OptionFormat.Multiplier);
        Speedrun_SpeedBoostDuration = FloatOptionItem.Create(Id + 13, "Speedrun_SpeedBoostDuration", new(0.5f, 60f, 0.5f), 2.5f, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetParent(Speedrun_SpeedBoostAfterTask)
            .SetValueFormat(OptionFormat.Seconds);

        Speedrun_ProtectAfterTask = BooleanOptionItem.Create(Id + 14, "Speedrun_ProtectAfterTask", true, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetHeader(true);
        Speedrun_ProtectDuration = FloatOptionItem.Create(Id + 15, "Speedrun_ProtectDuration", new(0.5f, 60f, 0.5f), 2.5f, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetParent(Speedrun_ProtectAfterTask)
            .SetValueFormat(OptionFormat.Seconds);
        Speedrun_ProtectOnlyOnce = BooleanOptionItem.Create(Id + 16, "Speedrun_ProtectOnlyOnce", true, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetParent(Speedrun_ProtectAfterTask);
        Speedrun_ProtectKcd = FloatOptionItem.Create(Id + 17, "Speedrun_ProtectKcd", new(0.5f, 60f, 0.5f), 5f, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetParent(Speedrun_ProtectAfterTask)
            .SetValueFormat(OptionFormat.Seconds);

        Speedrun_EndGameForTime = BooleanOptionItem.Create(Id + 18, "Speedrun_EndGameForTime", false, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetHeader(true);
        Speedrun_MaxTimeForTie = IntegerOptionItem.Create(Id + 19, "Speedrun_MaxTimeForTie", new(30, 3600, 15), 600, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetParent(Speedrun_EndGameForTime)
            .SetValueFormat(OptionFormat.Seconds);
        Speedrun_EnablePersonalTimer = BooleanOptionItem.Create(Id + 21, "Speedrun_EnablePersonalTimer", false, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetHeader(true);
        Speedrun_PersonalTimeLimit = IntegerOptionItem.Create(Id + 22, "Speedrun_PersonalTimeLimit", new(5, 600, 5), 60, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetParent(Speedrun_EnablePersonalTimer)
            .SetValueFormat(OptionFormat.Seconds);

        Speedrun_TaskFinishWins = BooleanOptionItem.Create(Id + 23, "Speedrun_TaskFinishWins", false, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetHeader(true);
        Speedrun_TaskWinnerCount = IntegerOptionItem.Create(Id + 24, "Speedrun_TaskWinnerCount", new(1, 127, 1), 1, TabGroup.ModSettings, false)
            .SetGameMode(CustomGameMode.Speedrun)
            .SetColor(ModeColor)
            .SetParent(Speedrun_TaskFinishWins);
    }


    public static readonly Color32 ModeColor = new(255, 251, 0, byte.MaxValue);
    internal readonly record struct RunnerProgress(int CompletedTasks, int TotalTasks, int Kills, int? FinishSeconds,
        bool IsProtected, bool IsEliminated, byte FinishPlace, long PersonalDeadline)
    {
        internal bool HasFinished => FinishSeconds.HasValue;
    }

    internal static readonly Dictionary<byte, RunnerProgress> Progress = [];
    internal static long StartedAt { get; private set; }
    internal static bool IntroRoleInfoActive { get; set; }
    internal static bool IsTaskRace => Options.CurrentGameMode == CustomGameMode.Speedrun && Speedrun_TaskFinishWins.GetBool();
    private static bool tasksAssigned;

    internal static bool IsTaskRaceFinisher(byte playerId) => IsTaskRace && Progress.GetValueOrDefault(playerId).HasFinished;

    internal static string GetTaskRaceDescription()
        => string.Format(GetString("Speedrun_TaskRaceDescription"), Speedrun_TaskWinnerCount.GetInt());

    public static void Init()
    {
        StartedAt = 0;
        IntroRoleInfoActive = true;
        tasksAssigned = false;
        Progress.Clear();
        RpcSetTasksPatch.ResetSpeedrunCommonTasks();
    }

    internal static void OnTasksAssigned()
    {
        foreach (var runner in Main.AllPlayerControls.Where(pc => pc.Is(CustomRoles.Runner) && !pc.IsDisconnected()))
        {
            Progress[runner.PlayerId] = Progress[runner.PlayerId] with { TotalTasks = runner.Data.Tasks.Count };
        }
        tasksAssigned = true;
        BeginGameplayIfReady();
    }

    internal static void BeginGameplayIfReady()
    {
        if (!tasksAssigned || !Main.IntroDestroyed || StartedAt != 0) return;

        StartedAt = GetTimeStamp();
        var alive = GetAliveRunners();
        foreach (var player in alive)
        {
            // Freeplay dummies are practice targets, so they have no task list or client timer.
            if (GameStates.IsFreePlay && !player.AmOwner) continue;
            var progress = Progress[player.PlayerId];
            if (progress.TotalTasks == 0 && player.GetRoleClass() is Runner runner)
            {
                runner.FinishTasks(player);
            }
            if (!GameStates.AirshipIsActive || Main.PlayerStates[player.PlayerId].HasSpawned)
                ResetPersonalTimer(player.PlayerId, StartedAt);
        }
        UpdateArrows(alive);
        SyncProgress();
    }

    internal static PlayerControl[] GetAliveRunners() => Main.AllAlivePlayerControls
        .Where(pc => pc.Is(CustomRoles.Runner) && !pc.IsDisconnected()).ToArray();

    internal static void ResetPersonalTimer(byte playerId, long now)
    {
        if (!Speedrun_EnablePersonalTimer.GetBool()) return;

        var progress = Progress[playerId];
        bool racingFinished = IsTaskRace && progress.HasFinished;
        // Give a killer the full action window after their normal cooldown.
        int cooldown = progress.HasFinished && !racingFinished
            ? (int)Math.Ceiling(Main.AllPlayerKillCooldown.GetValueOrDefault(playerId)) : 0;
        Progress[playerId] = progress with
        {
            PersonalDeadline = racingFinished ? 0 : now + Speedrun_PersonalTimeLimit.GetInt() + cooldown
        };
    }

    internal static void OnElimination(byte playerId, byte? killerId = null)
    {
        if (!Progress.TryGetValue(playerId, out var progress) || progress.IsEliminated) return;

        Progress[playerId] = progress with { IsEliminated = true, IsProtected = false, PersonalDeadline = 0 };
        SyncProgress(playerId);
        if (killerId.HasValue && killerId.Value != playerId && Progress.TryGetValue(killerId.Value, out var killer))
        {
            Progress[killerId.Value] = killer with { Kills = killer.Kills + 1 };
            ResetPersonalTimer(killerId.Value, GetTimeStamp());
            SyncProgress(killerId.Value);
        }

        var alive = GetAliveRunners();
        if (!IsTaskRace && Speedrun_RunnerKcdPerDeadPlayer.GetFloat() > 0f)
        {
            foreach (var runner in alive.Where(pc => Progress[pc.PlayerId].HasFinished))
            {
                runner.ResetKillCooldown();
                runner.MarkDirtySettings();
            }
        }

        TargetArrow.RemoveAllTarget(playerId);
        foreach (var runner in alive) TargetArrow.Remove(runner.PlayerId, playerId);
        UpdateArrows(alive);
        NotifyRoles(ForceLoop: true);
    }

    private static void UpdateArrows(PlayerControl[] alive)
    {
        if (!Speedrun_ArrowPlayers.GetBool() || alive.Length > Speedrun_ArrowPlayersPlayerLiving.GetInt()) return;

        foreach (var seer in alive)
            foreach (var target in alive)
                if (seer != target) TargetArrow.Add(seer.PlayerId, target.PlayerId);
    }

    internal static void SyncProgress(byte? playerId = null)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        long now = GetTimeStamp();
        CustomRpcTransport.SendPayload(CustomRPC.SyncSpeedrunData, writer =>
        {
            writer.Write(playerId.HasValue ? (byte)1 : (byte)Progress.Count);
            foreach (var (id, progress) in Progress)
            {
                if (playerId.HasValue && playerId.Value != id) continue;
                writer.Write(id);
                writer.Write((byte)progress.CompletedTasks);
                writer.Write((byte)progress.TotalTasks);
                writer.Write((byte)progress.Kills);
                writer.WritePacked(progress.FinishSeconds.GetValueOrDefault(-1) + 1);
                writer.Write(progress.IsProtected);
                writer.Write(progress.IsEliminated);
                writer.Write(progress.FinishPlace);
                writer.WritePacked(progress.PersonalDeadline == 0 ? 0 : (int)Math.Max(0, progress.PersonalDeadline - now) + 1);
            }
        });
    }

    internal static void ReceiveProgress(MessageReader reader)
    {
        int count = reader.ReadByte();
        long now = GetTimeStamp();
        for (int i = 0; i < count; i++)
        {
            byte id = reader.ReadByte();
            int completed = reader.ReadByte();
            int total = reader.ReadByte();
            int kills = reader.ReadByte();
            int finishTime = reader.ReadPackedInt32();
            bool isProtected = reader.ReadBoolean();
            bool isEliminated = reader.ReadBoolean();
            byte place = reader.ReadByte();
            int timeLeft = reader.ReadPackedInt32();
            Progress[id] = new(completed, total, kills, finishTime == 0 ? null : finishTime - 1,
                isProtected, isEliminated, place, timeLeft == 0 ? 0 : now + timeLeft - 1);
        }
    }

    private static bool CanSeeOtherTaskCounts(PlayerControl seer)
        => seer != null && (!seer.IsAlive() || Speedrun_AliveCanSeeOtherTaskCounts.GetBool());

    private static string FormatTaskCounts(RunnerProgress progress)
    {
        Color color = progress.IsEliminated ? Color.gray : progress.HasFinished ? Color.green : Color.yellow;
        return ColorString(color, $"({progress.CompletedTasks}/{progress.TotalTasks})");
    }

    internal static string GetProgressText(byte playerId, PlayerControl seer)
    {
        if (!Progress.TryGetValue(playerId, out var progress)) return string.Empty;

        string counts = seer?.PlayerId == playerId || CanSeeOtherTaskCounts(seer)
            ? FormatTaskCounts(progress) : string.Empty;
        if (!progress.HasFinished || IsTaskRace) return counts;

        string kills = ColorString(Color.red, string.Format(GetString("KillCount"), progress.Kills));
        return counts.Length == 0 ? kills : $"{counts} {kills}";
    }

    public static string GetGameState(bool forGameEnd = false)
    {
        var builder = new StringBuilder();
        if (forGameEnd) builder.AppendLine(ColorString(ModeColor, GetString("Speedrun_LeaderBoard")));

        var seer = PlayerControl.LocalPlayer;
        bool showOtherTaskCounts = forGameEnd || CanSeeOtherTaskCounts(seer);
        bool taskRace = IsTaskRace;
        var ranked = taskRace
            ? Progress.OrderByDescending(entry => entry.Value.HasFinished)
                .ThenBy(entry => entry.Value.FinishPlace)
                .ThenBy(entry => entry.Value.IsEliminated)
                .ThenByDescending(entry => showOtherTaskCounts ? entry.Value.CompletedTasks : 0)
                .ThenBy(entry => entry.Key)
            : Progress.OrderBy(entry => entry.Value.IsEliminated)
                .ThenByDescending(entry => entry.Value.HasFinished)
                .ThenByDescending(entry => entry.Value.Kills)
                .ThenBy(entry => entry.Value.FinishSeconds)
                .ThenByDescending(entry => showOtherTaskCounts ? entry.Value.CompletedTasks : 0)
                .ThenBy(entry => entry.Key);

        int rank = 0;
        foreach (var (id, progress) in ranked)
        {
            if (rank > 0) builder.AppendLine();
            rank++;
            var playerName = Main.AllPlayerNames.GetValueOrDefault(id, GetPlayerById(id)?.GetRealName() ?? string.Empty);
            builder.Append(forGameEnd && CustomWinnerHolder.WinnerIds.Contains(id) ? "<#c4aa02>★</color> " : $"{rank}. ");
            Color nameColor = taskRace && progress.HasFinished ? Color.green : Main.PlayerColors.GetValueOrDefault(id, Color.white);
            builder.Append(ColorString(nameColor, playerName.RemoveHtmlTags()));
            string eliminatedText = Main.PlayerStates.GetValueOrDefault(id)?.deathReason == PlayerState.DeathReason.OutOfTime
                ? "DeathReason.OutOfTime" : "Death";
            builder.Append(progress.IsEliminated
                ? $" ({ColorString(Color.red, GetString(eliminatedText))})"
                : $" ({ColorString(Color.green, GetString("Alive"))})");

            if (showOtherTaskCounts || seer?.PlayerId == id)
                builder.Append(' ').Append(FormatTaskCounts(progress));

            if (progress.HasFinished)
            {
                builder.Append(' ').AppendFormat(GetString("TaskFinishedSeconds"), progress.FinishSeconds);
                if (!taskRace) builder.Append(' ').AppendFormat(GetString("KillCount"), progress.Kills);
            }
        }
        return builder.ToString();
    }

    public static string GetRemainingPlayersText() => IsTaskRace
        ? string.Join("\n",
            string.Format(GetString("Remaining.SpeedrunFinishers"), Progress.Values.Count(p => p.HasFinished)),
            string.Format(GetString("Remaining.SpeedrunRacing"), Progress.Values.Count(p => !p.IsEliminated && !p.HasFinished)),
            string.Format(GetString("Remaining.SpeedrunEliminated"), Progress.Values.Count(p => p.IsEliminated && !p.HasFinished)))
        : string.Join("\n",
            string.Format(GetString("Remaining.SpeedrunAliveKiller"), Progress.Values.Count(p => !p.IsEliminated && p.HasFinished)),
            string.Format(GetString("Remaining.SpeedrunAliveRunner"), Progress.Values.Count(p => !p.IsEliminated && !p.HasFinished)),
            string.Format(GetString("Remaining.SpeedrunDeadKiller"), Progress.Values.Count(p => p.IsEliminated && p.HasFinished)),
            string.Format(GetString("Remaining.SpeedrunDeadRunner"), Progress.Values.Count(p => p.IsEliminated && !p.HasFinished)));
}

class SpeedrunGameEndPredicate : GameEndPredicate
{
    private long lastTimerUpdate;
    private bool processingTimeouts;

    public override bool CheckForEndGame(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (SpeedrunManager.StartedAt == 0 || processingTimeouts) return false;

        long now = GetTimeStamp();
        var alive = UpdatePersonalTimers(SpeedrunManager.GetAliveRunners(), now);
        bool timeExpired = SpeedrunManager.Speedrun_EndGameForTime.GetBool()
            && now - SpeedrunManager.StartedAt >= SpeedrunManager.Speedrun_MaxTimeForTie.GetInt();

        if (SpeedrunManager.IsTaskRace)
        {
            int winnerCount = SpeedrunManager.Speedrun_TaskWinnerCount.GetInt();
            var finishers = SpeedrunManager.Progress.Where(entry => entry.Value.HasFinished);
            if (!timeExpired && finishers.Count() < winnerCount
                && alive.Any(pc => !SpeedrunManager.Progress[pc.PlayerId].HasFinished)) return false;

            CustomWinnerHolder.WinnerIds.UnionWith(finishers.OrderBy(entry => entry.Value.FinishPlace)
                .Take(winnerCount).Select(entry => entry.Key));
        }
        else
        {
            if (alive.Length > 1 && !timeExpired) return false;
            CustomWinnerHolder.WinnerIds.UnionWith(alive.Select(pc => pc.PlayerId));
        }
        if (CustomWinnerHolder.WinnerIds.Count == 0)
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.None);
        // A disconnected runner can still be cached as a crewmate on a vanilla
        // client. Keep the impostor win reason so only our winner views qualify.
        Main.DoBlockNameChange = true;
        return true;
    }

    private PlayerControl[] UpdatePersonalTimers(PlayerControl[] alive, long now)
    {
        if (!SpeedrunManager.Speedrun_EnablePersonalTimer.GetBool() || lastTimerUpdate == now) return alive;
        lastTimerUpdate = now;

        var expired = alive.Where(pc =>
        {
            long deadline = SpeedrunManager.Progress[pc.PlayerId].PersonalDeadline;
            return deadline != 0 && deadline <= now;
        }).ToArray();
        if (expired.Length > 0)
        {
            // A local murder callback checks for game end synchronously. Resolve
            // everyone due this second before choosing the last survivor.
            processingTimeouts = true;
            try
            {
                foreach (var player in expired)
                {
                    player.SetDeathReason(PlayerState.DeathReason.OutOfTime);
                    player.SetRealKiller(player);
                    player.RpcMurderPlayer(player);
                }
            }
            finally
            {
                processingTimeouts = false;
            }
            alive = SpeedrunManager.GetAliveRunners();
        }
        if (alive.Any(pc => SpeedrunManager.Progress[pc.PlayerId].PersonalDeadline != 0))
            NotifyRoles(ForceLoop: false);
        return alive;
    }
}
