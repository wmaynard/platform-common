using System;
using System.Net.Mail;
using System.Text.RegularExpressions;
using RCL.Logging;

namespace Rumble.Platform.Common.Utilities;

public static class EmailRegex
{
    // Offshoot of RFC 5322 via emailregex.com
    // Site is down at the moment; archive: https://web.archive.org/web/20221223174323/http://emailregex.com/
    private const string REGEX = "^(?(\")(\".+?(?<!\\\\)\"@)|(([0-9a-z]((\\.(?!\\.))|[-!#\\$%&'\\*\\+/=\\?\\^`\\{\\}\\|~\\w])*)(?<=[0-9a-z])@))(?(\\[)(\\[(\\d{1,3}\\.){3}\\d{1,3}\\])|(([0-9a-z][-\\w]*[0-9a-z]*\\.)+[a-z0-9][\\-a-z0-9]{0,22}[a-z0-9]))$";
    
    /// <summary>
    /// Validates an email address against both a REGEX and the System.Net.Mail.MailAddress.  Returns false if the email is invalid.
    /// </summary>
    public static bool IsValid(string address)
    {
        try
        {
            return Regex.IsMatch(input: address, pattern: REGEX) & MailAddress.TryCreate(address, out _);
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Email regex validation failed.", data: new
            {
                Email = address
            }, exception: e);
        }

        return false;
    }
}