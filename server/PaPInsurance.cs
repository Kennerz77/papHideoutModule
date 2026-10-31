using System.Reflection;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Services.Commerce;

namespace PaPHideoutServer;

/// <summary>
/// SPT gives every item in a mail message new ids, and insured guns come back by mail, so an infused gun that was
/// lost and insured came back as a plain gun. This follows the ids: whatever was infused before the mail was built is
/// infused under its new id afterwards.
/// </summary>
internal sealed class MailIdsPatch : AbstractPatch
{
    protected override MethodBase? GetTargetMethod() =>
        typeof(MailSendService).GetMethod("ProcessItemsBeforeAddingToMail", BindingFlags.Instance | BindingFlags.NonPublic);

    [PatchPrefix]
    private static void Prefix(SendMessageDetails messageDetails, out List<string>? __state)
    {
        __state = null;
        try
        {
            PaPService? service = PaPService.Instance;
            if (service == null || messageDetails.Items is not { Count: > 0 } items)
                return;
            if (items.Any(i => service.IsInfused(messageDetails.RecipientId, i.Id.ToString())))
                __state = items.Select(i => i.Id.ToString()).ToList();
        }
        catch
        {
            // never let this get in the way of mail
        }
    }

    [PatchPostfix]
    private static void Postfix(SendMessageDetails messageDetails, List<string>? __state)
    {
        // The details now hold the copies with new ids, in the same order
        try
        {
            if (__state == null || messageDetails.Items is not { } items || items.Count != __state.Count)
                return;
            for (int i = 0; i < items.Count; i++)
            {
                string now = items[i].Id.ToString();
                if (now != __state[i])
                    PaPService.Instance?.Moved(messageDetails.RecipientId, __state[i], now);
            }
        }
        catch
        {
            // never let this get in the way of mail
        }
    }
}
