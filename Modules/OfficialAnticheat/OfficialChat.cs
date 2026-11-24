using AmongUs.QuickChat;
using Hazel;
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TOHE;

/// <summary>Host output uses custom RPCs for mod clients and a reliable name transaction for vanilla.</summary>
internal static class OfficialChat
{
    private static readonly object TransactionLock = new();
    private const int SoftPacketBytes = 1000;
    private const int HardPacketBytes = OfficialPacketCodec.HardLimit;
    private const float ChatTitleBodyGap = 0.233f;
    // Vanilla waits for an empty bubble name to be filled. A zero-width space keeps the heading invisible.
    private const string HiddenHeading = "\u200B";
    // Reserve enough height below the visible text and hide vanilla's appended color name.
    private const string PageFooter = "\n\n<#0000>.";
    internal readonly record struct SentPage(byte SpeakerId, PlayerControl Recipient, string Heading, string Text);
    private readonly record struct RestoredName(PlayerControl Player, string Name, int ByteCount);
    internal static PlayerControl DisplayActor { get; private set; }
    internal static string DisplayTitle { get; private set; }

    // Transport only. Higher-level modules decide whether a delivered page belongs in history.
    internal static SentPage[] Send(PlayerControl actor, string text, byte recipient = byte.MaxValue, string title = null,
        bool preserveDeadActor = false, Action<MessageWriter> send = null, bool privateTransport = false)
        => SendTo(actor, text, title, preserveDeadActor, send, privateTransport, recipient);

    internal static SentPage[] SendPrivate(PlayerControl actor, string text, IReadOnlyCollection<PlayerControl> recipients, string title)
    {
        var batch = OfficialAnticheatPolicy.Enabled ? new OfficialPacketBuilder.TargetedTransactionBatch() : null;
        var pages = SendTo(actor, text, title, preserveDeadActor: false,
            send: batch == null ? null : batch.Add, privateTransport: true, recipients: recipients);
        batch?.Send();
        return pages;
    }

    private static SentPage[] SendTo(PlayerControl actor, string text, string title, bool preserveDeadActor,
        Action<MessageWriter> send, bool privateTransport, byte recipient = byte.MaxValue,
        IReadOnlyCollection<PlayerControl> recipients = null)
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || actor?.Data == null) return [];
        var sentPages = new List<SentPage>();
        lock (TransactionLock)
        {
            string senderName = actor.Data.PlayerName;
            title = ChatRichText.Compact(title ?? senderName, "OfficialChat.Title");
            text = ChatRichText.Compact(text, "OfficialChat.Body");
            string titlePadding = null;
            byte speakerId = actor.PlayerId;
            var connectedPlayers = Main.AllPlayerControls.Where(p => p?.Data != null && !p.Data.Disconnected)
                .OrderBy(p => p.PlayerId).ToArray();
            // System messages need a living actor. Player chat uses the actual speaker.
            if (!preserveDeadActor)
                actor = connectedPlayers.FirstOrDefault(p => p == actor && !p.Data.IsDead)
                    ?? connectedPlayers.FirstOrDefault(p => !p.Data.IsDead) ?? actor;
            var context = OfficialSessionContext.Capture();
            bool Current() => context.IsCurrent() && client.AmHost;

            foreach (var seer in (recipients ?? connectedPlayers).Where(p => p?.Data != null && !p.Data.Disconnected
                && (recipient == byte.MaxValue || p.PlayerId == recipient)).OrderBy(p => p.PlayerId))
            {
                if (!Current()) return sentPages.ToArray();
                if (seer.AmOwner)
                {
                    var (localHeading, localPages) = ShowLocal(actor, text, title);
                    foreach (var page in localPages)
                        sentPages.Add(new(speakerId, seer, localHeading, page));
                    continue;
                }
                int target = seer.GetClientId();
                if (target < 0) continue; // A missing private recipient must never become a broadcast.
                if (seer.IsModded())
                {
                    try
                    {
                        var (modHeading, _, modPages) = LocalPages(text, title, includeTitleInBudget: privateTransport);
                        if (privateTransport)
                        {
                            // Use the same SystemChat envelope inside a server-targeted
                            // GameDataTo. The custom queue's logical target is client-side only.
                            foreach (string page in modPages)
                            {
                                using var packet = new OfficialPacketBuilder.BoundedWriter(SendOption.Reliable);
                                var writer = packet.Writer;
                                writer.StartMessage(6);
                                writer.Write(client.GameId);
                                writer.WritePacked(target);
                                writer.StartMessage(2);
                                writer.WritePacked(PlayerControl.LocalPlayer.NetId);
                                writer.Write(RpcPayloadSnapshot.OuterCallId);
                                writer.Write(target);
                                writer.Write((uint)CustomRPC.SystemChat);
                                writer.Write(actor.PlayerId);
                                writer.Write(modHeading);
                                writer.Write(page);
                                writer.EndMessage();
                                writer.EndMessage();
                                if (send == null) OfficialNetworkSend.Send(client, writer);
                                else send(writer);
                                sentPages.Add(new(speakerId, seer, modHeading, page));
                            }
                            continue;
                        }
                        CustomRpcTransport.SendPayload(CustomRPC.SystemChat, writer =>
                        {
                            writer.Write(actor.PlayerId);
                            writer.Write(title);
                            writer.Write(text);
                        }, target);
                        foreach (var page in modPages)
                            sentPages.Add(new(speakerId, seer, modHeading, page));
                    }
                    catch (Exception error)
                    {
                        Logger.Error($"Custom chat aborted: sender={senderName} ({speakerId}), " +
                            $"recipient={seer.Data?.PlayerName} ({seer.PlayerId}), title={title}, message={text}\n{error}",
                            "OfficialChat", escapeCRLF: false);
                    }
                    continue;
                }
                string heading = NonEmptyName(title), body = text;
                try
                {
                    var (senderNameToRestore, carrierNameToRestore) = SelectVanillaActors(actor, connectedPlayers, seer, preserveDeadActor);
                    var (sender, actorRestore, actorRestoreBytes) = senderNameToRestore;
                    var (carrier, carrierRestore, carrierRestoreBytes) = carrierNameToRestore;
                    // Preflight bounded names before creating a Hazel buffer. Large headings become body text.
                    int inputNameBytes = Encoding.UTF8.GetByteCount(heading);
                    int textBytes = Encoding.UTF8.GetByteCount(body);
                    if (actorRestoreBytes > HardPacketBytes || carrierRestoreBytes > HardPacketBytes)
                        throw new InvalidOperationException("The recipient name restoration exceeds the packet budget.");
                    if (inputNameBytes > 256)
                    {
                        body = heading + "\n" + body;
                        heading = HiddenHeading;
                    }
                    // Use one byte per SetName field to measure framing without serializing empty or oversized names.
                    const string probeName = "x";
                    int nameFieldCount = sender == carrier ? 2 : 4;
                    int remaining;
                    using (var baseline = BuildTransaction(sender, carrier, target, probeName, probeName, probeName, probeName))
                        remaining = HardPacketBytes - baseline.Writer.Length + nameFieldCount;
                    remaining -= StringBytesBeyondEmpty(carrierRestoreBytes);
                    if (sender != carrier) remaining -= StringBytesBeyondEmpty(actorRestoreBytes);

                    (string Padding, int Limit, string[] Pages) PreparePages(string pageHeading, string pageBody)
                    {
                        string pagePadding = pageHeading == title ? (titlePadding ??= TitlePadding(title)) : string.Empty;
                        int limit = remaining;
                        if (sender != carrier) limit -= StringBytesBeyondEmpty(Encoding.UTF8.GetByteCount(pageHeading));
                        // The empty-string prefix is already in the baseline. A body of 128 bytes or more needs another byte.
                        if (limit >= 128) limit--;
                        limit -= Encoding.UTF8.GetByteCount(pagePadding + PageFooter);
                        return (pagePadding, limit, TryPaginate(pageBody, limit));
                    }

                    var (padding, available, pages) = PreparePages(heading, body);
                    if (pages == null && !preserveDeadActor)
                    {
                        // Keep the content readable when the title or paired tags cannot fit beside the restored names.
                        body = body.RemoveHtmlTags();
                        string plainHeading = heading.RemoveHtmlTags();
                        if (heading != HiddenHeading && !string.IsNullOrWhiteSpace(plainHeading))
                            body = plainHeading + "\n" + body;
                        heading = HiddenHeading;
                        (padding, available, pages) = PreparePages(heading, body);
                    }
                    if (pages == null)
                        throw new InvalidOperationException("The name restoration leaves too little room for the message.");

                    Logger.Info($"recipient={seer.PlayerId}, inputNameUtf8Bytes={inputNameBytes}, " +
                        $"setHeadingUtf8Bytes={(sender == carrier ? 0 : Encoding.UTF8.GetByteCount(heading))}, " +
                        $"actorRestoreUtf8Bytes={actorRestoreBytes}, carrierRestoreUtf8Bytes={carrierRestoreBytes}, " +
                        $"textUtf8Bytes={textBytes}, paginatedBodyUtf8Bytes={Encoding.UTF8.GetByteCount(body)}, " +
                        $"pageLimitUtf8Bytes={available}, sender={sender.PlayerId}, carrier={carrier.PlayerId}", "OfficialChat.Pagination");
                    Logger.Info($"recipient={seer.PlayerId}, pageCount={pages.Length}", "OfficialChat.Pagination");
                    int pageIndex = 0;
                    foreach (string page in pages)
                    {
                        if (!Current()) return sentPages.ToArray();
                        using var packet = BuildTransaction(sender, carrier, target, heading, padding + page + PageFooter, actorRestore, carrierRestore);
                        if (packet.Writer.Length > HardPacketBytes)
                            throw new InvalidOperationException("The complete chat transaction exceeds 1200 bytes.");
                        Logger.Info($"recipient={seer.PlayerId}, page={++pageIndex}/{pages.Length}, " +
                            $"setCarrierNameUtf8Bytes={Encoding.UTF8.GetByteCount(padding + page + PageFooter)}, " +
                            $"packetBytes={packet.Writer.Length}", "OfficialChat.Pagination");
                        if (send == null) OfficialNetworkSend.Send(client, packet.Writer);
                        else send(packet.Writer);
                        sentPages.Add(new(sender.PlayerId, seer, heading, page));
                    }
                }
                catch (Exception error)
                {
                    Logger.Error($"Chat transaction aborted: sender={senderName} ({speakerId}), " +
                        $"recipient={seer.Data?.PlayerName} ({seer.PlayerId}), title={title}, message={text}\n{error}",
                        "OfficialChat", escapeCRLF: false);
                }
            }
        }
        return sentPages.ToArray();
    }

    private static (RestoredName Sender, RestoredName Carrier) SelectVanillaActors(PlayerControl actor,
        PlayerControl[] connectedPlayers, PlayerControl seer, bool preserveDeadActor)
    {
        RestoredName PrepareRestore(PlayerControl player)
        {
            string name = NonEmptyName(ChatRichText.Compact(VisibleName(player, seer),
                $"OfficialChat.RestoreCandidate, player={player.PlayerId}, recipient={seer.PlayerId}"));
            return new(player, name, Encoding.UTF8.GetByteCount(name));
        }

        if (preserveDeadActor)
            return (PrepareRestore(actor), PrepareRestore(connectedPlayers.FirstOrDefault(p => p != actor) ?? actor));

        // In-game names can vary per recipient. Rank the compressed strings that will be written to the packet.
        var byRestoreSize = connectedPlayers.Select(PrepareRestore).OrderBy(p => p.ByteCount).ToArray();
        var sender = byRestoreSize.FirstOrDefault(p => !p.Player.Data.IsDead);
        if (sender.Player == null) sender = PrepareRestore(actor);
        var carrier = byRestoreSize.FirstOrDefault(p => p.Player != sender.Player);
        return (sender, carrier.Player == null ? sender : carrier);
    }

    internal static string VisibleName(PlayerControl actor, PlayerControl seer)
        // Role-name views can survive the round; lobby chat must restore current tags.
        => !GameStates.IsLobby && Main.LastNotifyNames.TryGetValue((actor.PlayerId, seer.PlayerId), out var name)
            ? name : actor.Data.PlayerName;

    internal static (string Heading, string[] Pages) ShowLocal(PlayerControl actor, string text, string title, bool censor = false,
        bool textHasNamePadding = false, bool allowDeadSpeaker = false)
    {
        if (actor?.Data == null || !DestroyableSingleton<HudManager>.Instance) return (title ?? string.Empty, []);
        string speakerName = actor.Data.PlayerName;
        title ??= speakerName;
        text ??= string.Empty;
        var previousActor = DisplayActor;
        string previousTitle = DisplayTitle;
        bool showDeadSpeaker = false;
        try
        {
            var (heading, padding, pages) = LocalPages(text, title, includeTitlePadding: !textHasNamePadding);
            showDeadSpeaker = allowDeadSpeaker && actor.Data.IsDead && PlayerControl.LocalPlayer?.Data != null
                && !PlayerControl.LocalPlayer.Data.IsDead;
            // Override only the bubble's name at the native presentation boundary. Player data,
            // cosmetics, notifications and name caches continue to see the real player name.
            DisplayActor = actor;
            DisplayTitle = heading;
            if (showDeadSpeaker) actor.Data.IsDead = false;
            foreach (string page in pages)
                DestroyableSingleton<HudManager>.Instance.Chat.AddChat(actor, padding + page + PageFooter, censor);
            return (heading, pages);
        }
        catch (Exception error)
        {
            Logger.Error($"Local chat display failed: speaker={speakerName} ({actor.PlayerId}), " +
                $"title={title}, message={text}\n{error}", "OfficialChat", escapeCRLF: false);
            throw;
        }
        finally
        {
            if (showDeadSpeaker) actor.Data.IsDead = true;
            DisplayActor = previousActor;
            DisplayTitle = previousTitle;
        }
    }

    private static (string Heading, string Padding, string[] Pages) LocalPages(string text, string title, bool includeTitlePadding = true,
        bool includeTitleInBudget = false)
    {
        if (Encoding.UTF8.GetByteCount(title) > 256)
        {
            text = title + "\n" + text;
            title = string.Empty;
        }
        string padding = includeTitlePadding ? TitlePadding(title) : string.Empty;
        int available = SoftPacketBytes - Encoding.UTF8.GetByteCount(padding + PageFooter);
        if (includeTitleInBudget) available -= Encoding.UTF8.GetByteCount(title);
        return (title, padding, Paginate(text, available));
    }

    // Hazel prefixes UTF-8 strings with their packed byte count. Bounded packet strings use at most two prefix bytes.
    private static int StringBytesBeyondEmpty(int utf8Bytes) => utf8Bytes + (utf8Bytes >= 128 ? 1 : 0);

    // Both sides of the native ChatBubble place NameText at Y=.358 and TextArea at Y=.125.
    // Measure with the stock width and fonts so wrapped or tall headings get enough room.
    private static string TitlePadding(string title)
    {
        if (string.IsNullOrEmpty(title) || title == HiddenHeading) return string.Empty;

        int lines = Regex.Matches(title, @"\n|<br\s*/?>", RegexOptions.IgnoreCase).Count;
        var prefab = DestroyableSingleton<HudManager>.Instance?.Chat?.chatBubblePool?.Prefab?.TryCast<ChatBubble>();
        if (prefab?.NameText == null || prefab.TextArea == null) return new('\n', lines);

        var name = prefab.NameText;
        float height = name.GetPreferredValues(title, name.rectTransform.rect.width, float.PositiveInfinity).y;
        if (!float.IsFinite(height) || height <= ChatTitleBodyGap) return new('\n', lines);

        var body = prefab.TextArea;
        float width = body.rectTransform.rect.width;
        float lineHeight = body.GetPreferredValues(".\n.", width, float.PositiveInfinity).y
            - body.GetPreferredValues(".", width, float.PositiveInfinity).y;
        if (lineHeight <= 0f || !float.IsFinite(lineHeight)) return new('\n', lines);

        int measuredLines = (int)MathF.Min(HardPacketBytes, MathF.Ceiling((height - ChatTitleBodyGap) / lineHeight));
        return new('\n', Math.Max(lines, measuredLines));
    }

    private static OfficialPacketBuilder.BoundedWriter BuildTransaction(PlayerControl actor, PlayerControl carrier, int target, string heading,
        string body, string actorRestore, string carrierRestore)
    {
        var owner = new OfficialPacketBuilder.BoundedWriter(SendOption.Reliable);
        var writer = owner.Writer;
        try
        {
            writer.StartMessage(6);
            writer.Write(AmongUsClient.Instance.GameId);
            writer.WritePacked(target);
            if (actor != carrier) WriteName(writer, actor, heading);
            WriteName(writer, carrier, body);
            writer.StartMessage(2);
            writer.WritePacked(actor.NetId);
            writer.Write((byte)RpcCalls.SendQuickChat);
            // Use the current native serializer: PlayerId is a valid root phrase, unlike Empty.
            QuickChatNetData.Serialize(new QuickChatPhraseBuilderResult(QuickChatPhraseType.PlayerId,
                StringNames.None, carrier.PlayerId, null), writer);
            writer.EndMessage();
            WriteName(writer, carrier, carrierRestore);
            if (actor != carrier) WriteName(writer, actor, actorRestore);
            writer.EndMessage();
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    private static string NonEmptyName(string name) => string.IsNullOrEmpty(name) ? HiddenHeading : name;

    internal static void WriteName(MessageWriter writer, PlayerControl player, string name)
    {
        writer.StartMessage(2);
        writer.WritePacked(player.NetId);
        writer.Write((byte)RpcCalls.SetName);
        writer.Write(player.Data.NetId);
        writer.Write(NonEmptyName(name));
        writer.EndMessage();
    }

    internal static string[] Paginate(string text, int maxUtf8Bytes)
        => TryPaginate(text, maxUtf8Bytes)
            ?? throw new InvalidOperationException("A text element or rich text tag exceeds the chat budget.");

    // Keep grapheme clusters and paired TMP tags intact. A null result means no complete set of pages fits.
    private static string[] TryPaginate(string text, int maxUtf8Bytes)
    {
        if (maxUtf8Bytes < 0) return null;
        var active = new List<(string Name, string Open)>();
        var pages = new List<string>();
        var page = new StringBuilder();
        bool hasContent = false;
        foreach (string token in Tokens(text ?? string.Empty))
        {
            var next = new List<(string Name, string Open)>(active);
            bool literal = active.Count > 0 && active[^1].Name == "noparse";
            var tag = !literal || token.Equals("</noparse>", StringComparison.OrdinalIgnoreCase)
                ? Regex.Match(token, @"^<(/?)([a-zA-Z-]+)(?:[ =][^<>]*)?>$")
                : Match.Empty;
            bool shorthandColor = !literal && ChatRichText.IsShorthandColor(token);
            if (shorthandColor) next.Add(("color", token));
            else if (tag.Success)
            {
                string name = tag.Groups[2].Value.ToLowerInvariant();
                if (tag.Groups[1].Value == "/")
                {
                    int at = next.FindLastIndex(t => t.Name == name);
                    if (at >= 0) next.RemoveRange(at, next.Count - at);
                }
                else if (name is "b" or "i" or "u" or "s" or "color" or "size" or "align" or "font"
                    or "mark" or "link" or "sub" or "sup" or "voffset" or "cspace" or "mspace" or "line-height"
                    or "indent" or "line-indent" or "margin" or "style" or "nobr" or "noparse" or "allcaps") next.Add((name, token));
            }
            string close = string.Concat(next.AsEnumerable().Reverse().Select(t => $"</{t.Name}>"));
            if (Encoding.UTF8.GetByteCount(page.ToString() + token + close) > maxUtf8Bytes)
            {
                if (!hasContent) return null;
                pages.Add(page + string.Concat(active.AsEnumerable().Reverse().Select(t => $"</{t.Name}>")));
                page.Clear();
                foreach (var open in active) page.Append(open.Open);
                hasContent = false;
                if (Encoding.UTF8.GetByteCount(page.ToString() + token + close) > maxUtf8Bytes)
                    return null;
            }
            page.Append(token);
            active = next;
            hasContent |= !tag.Success && !shorthandColor;
        }
        if (page.Length != 0 || string.IsNullOrEmpty(text))
            pages.Add(page + string.Concat(active.AsEnumerable().Reverse().Select(t => $"</{t.Name}>")));
        return pages.ToArray();
    }

    private static IEnumerable<string> Tokens(string text)
    {
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '<')
            {
                int end = text.IndexOf('>', i);
                if (end >= 0) { yield return text[i..(end + 1)]; i = end + 1; continue; }
            }
            string element = StringInfo.GetNextTextElement(text, i);
            yield return element;
            i += element.Length;
        }
    }
}

[HarmonyPatch(typeof(ChatController), nameof(ChatController.SetChatBubbleName))]
internal static class OfficialChatNamePatch
{
    private static bool Prefix([HarmonyArgument(0)] ChatBubble bubble,
        [HarmonyArgument(1)] NetworkedPlayerInfo playerInfo, [HarmonyArgument(2)] bool isDead,
        [HarmonyArgument(3)] bool didVote, [HarmonyArgument(4)] Color nameColor)
    {
        if (OfficialChat.DisplayActor == null || OfficialChat.DisplayActor.Data != playerInfo) return true;
        bubble.SetName(OfficialChat.DisplayTitle, isDead, didVote, nameColor);
        return false;
    }
}
