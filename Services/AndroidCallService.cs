using Android;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.Provider;
using AndroidX.Core.Content;
using Visor.Contract;

namespace Visor.Services;

public class AndroidCallService : ICallService
{
    public Task<CallResult> PlaceCallAsync(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult(new CallResult { Message = "I need a contact name or number first." });
        }

        var context = global::Android.App.Application.Context;
        string normalizedTarget = target.Trim();
        string? number = LooksLikePhoneNumber(normalizedTarget)
            ? normalizedTarget
            : ResolvePhoneNumber(context, normalizedTarget);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Task.FromResult(new CallResult { Message = $"I couldn't find {normalizedTarget} in your contacts." });
        }

        bool hasCallPermission = ContextCompat.CheckSelfPermission(context, Manifest.Permission.CallPhone) == Permission.Granted;
        var intentAction = hasCallPermission ? Intent.ActionCall : Intent.ActionDial;
        var intent = new Intent(intentAction, Android.Net.Uri.Parse($"tel:{number}"));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);

        string message = hasCallPermission
            ? $"Calling {normalizedTarget}."
            : $"Opening the dialer for {normalizedTarget}.";

        return Task.FromResult(new CallResult { Success = true, Message = message });
    }

    private static bool LooksLikePhoneNumber(string value)
    {
        int digits = value.Count(char.IsDigit);
        return digits >= 3 && value.All(c => char.IsDigit(c) || c is '+' or '-' or '(' or ')' or ' ');
    }

    private static string? ResolvePhoneNumber(Context context, string target)
    {
        using ICursor? cursor = context.ContentResolver?.Query(
            ContactsContract.CommonDataKinds.Phone.ContentUri,
            new[]
            {
                ContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName,
                ContactsContract.CommonDataKinds.Phone.Number
            },
            null,
            null,
            null);

        if (cursor is null)
        {
            return null;
        }

        int nameIndex = cursor.GetColumnIndex(ContactsContract.CommonDataKinds.Phone.InterfaceConsts.DisplayName);
        int numberIndex = cursor.GetColumnIndex(ContactsContract.CommonDataKinds.Phone.Number);
        string normalizedTarget = target.Trim();
        string? partialMatch = null;

        while (cursor.MoveToNext())
        {
            string? displayName = cursor.GetString(nameIndex);
            string? phoneNumber = cursor.GetString(numberIndex);
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(phoneNumber))
            {
                continue;
            }

            if (displayName.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return phoneNumber;
            }

            if (partialMatch is null && displayName.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                partialMatch = phoneNumber;
            }
        }

        return partialMatch;
    }
}
