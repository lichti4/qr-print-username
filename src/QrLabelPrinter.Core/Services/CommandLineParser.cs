using System.Text;
using QrLabelPrinter.Models;

namespace QrLabelPrinter.Services;

public sealed record CommandLineResult(SapUser? User, IReadOnlyList<string> Errors)
{
    public bool IsValid => User is not null && Errors.Count == 0;
}

/// <summary>
/// Liest --sap-user, --firstname, --lastname (auch in der Form --name=wert).
/// Die Werte werden unverändert übernommen (nur Trim und Unicode-Normalisierung FormC).
/// </summary>
public static class CommandLineParser
{
    public const int MaxUsernameLength = 12;   // SAP-Benutzer-ID (BNAME)
    public const int MaxNameLength = 40;       // SAP NAME_FIRST / NAME_LAST

    private const string OptSapUser = "--sap-user";
    private const string OptFirstName = "--firstname";
    private const string OptLastName = "--lastname";

    private static readonly string[] KnownOptions = [OptSapUser, OptFirstName, OptLastName];

    public static CommandLineResult Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string? name = null;
            string? value = null;

            int eq = arg.IndexOf('=');
            string head = eq > 0 ? arg[..eq] : arg;

            if (!KnownOptions.Contains(head, StringComparer.OrdinalIgnoreCase))
                continue; // unbekannte Argumente werden ignoriert

            name = head.ToLowerInvariant();
            if (eq > 0)
            {
                value = arg[(eq + 1)..];
            }
            else if (i + 1 < args.Count && !KnownOptions.Contains(args[i + 1], StringComparer.OrdinalIgnoreCase))
            {
                value = args[++i];
            }
            else
            {
                errors.Add($"Dem Parameter '{name}' wurde kein Wert übergeben.");
                continue;
            }

            values[name] = value;
        }

        string username = Get(values, OptSapUser, "SAP-Username (--sap-user)", errors);
        string first = Get(values, OptFirstName, "Vorname (--firstname)", errors);
        string last = Get(values, OptLastName, "Nachname (--lastname)", errors);

        if (username.Length > 0)
        {
            if (username.Length > MaxUsernameLength)
                errors.Add($"Der SAP-Username ist zu lang (maximal {MaxUsernameLength} Zeichen).");
            else if (username.Any(c => c <= ' ' || c > '~'))
                errors.Add("Der SAP-Username enthält ungültige Zeichen (erlaubt sind nur sichtbare ASCII-Zeichen ohne Leerzeichen).");
        }

        CheckName(first, "Vorname", errors);
        CheckName(last, "Nachname", errors);

        if (errors.Count > 0) return new CommandLineResult(null, errors);
        return new CommandLineResult(new SapUser(username, first, last), errors);
    }

    private static string Get(Dictionary<string, string> values, string key, string label, List<string> errors)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            if (!errors.Any(e => e.Contains($"'{key}'"))) errors.Add($"Der Parameter {label} fehlt.");
            return "";
        }

        string v = raw.Normalize(NormalizationForm.FormC).Trim();
        if (v.Length == 0) errors.Add($"Der Parameter {label} ist leer.");
        return v;
    }

    private static void CheckName(string value, string label, List<string> errors)
    {
        if (value.Length == 0) return;
        if (value.Length > MaxNameLength)
            errors.Add($"Der {label} ist zu lang (maximal {MaxNameLength} Zeichen).");
        else if (value.Any(char.IsControl))
            errors.Add($"Der {label} enthält ungültige Steuerzeichen.");
    }
}
