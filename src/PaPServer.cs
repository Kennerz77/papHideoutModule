using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using SPT.Reflection.Utils;

namespace PaPHideoutModule
{
    /// <summary>
    /// Sends PaP item events through the game's own operation pipeline, so any inventory changes the server
    /// makes (money taken, weapon destroyed) are applied to the client's stash as well.
    /// </summary>
    internal static class PaPServer
    {
        public const string Upgrade = "PaPUpgrade";
        public const string Strip = "PaPStrip";
        public const string Destroy = "PaPDestroy";
        public const string Built = "PaPBuilt";
        public const string BotLoot = "PaPBotLoot";
        public const string RestartQuest = "PaPRestartQuest";

        /// <summary>done receives null if the request couldn't be sent.</summary>
        public static void Send(string action, string itemId, Action<IResult> done) =>
            Send(new PaPItemOperation { Action = action, item = itemId }, done);

        public static void Send(string action, List<string> itemIds, Action<IResult> done) =>
            Send(new PaPItemOperation { Action = action, items = itemIds }, done);

        /// <summary>Pack-a-Punch a weapon; looks-only (no weapon bonuses) is charged the smaller fee.</summary>
        public static void SendUpgrade(string weaponId, bool cosmetic, Action<IResult> done) =>
            Send(new PaPItemOperation { Action = Upgrade, item = weaponId, cosmetic = cosmetic }, done);

        private static void Send(PaPItemOperation operation, Action<IResult> done)
        {
            ClientBackendSession session = ClientAppUtils.GetMainApp()?.GetClientBackEndSession() as ClientBackendSession;
            if (session == null)
            {
                done(null);
                return;
            }
            session.SendOperationRightNow(operation, result => done(result));
        }

        /// <summary>Item event body; field names match the server's PaPItemRequest.</summary>
        private class PaPItemOperation
        {
            public string Action;
            public string item;
            public List<string> items;
            public bool cosmetic;
        }
    }
}
