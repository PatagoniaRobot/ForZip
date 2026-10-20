// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================

namespace ForZip.Cli;

public class CommandParser
{
    private readonly List<string> _args;
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positionals = new();

    /// <summary>Cada opción con su valor, en el orden en que llegó (las repetidas, todas).</summary>
    private readonly List<(string Key, string Value)> _occurrences = new();

    /// <summary>Opciones que llegaron sin valor (al final, o seguidas de otra opción).</summary>
    private readonly HashSet<string> _withoutValue = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Opciones cuyo valor es una contraseña: toman siempre el argumento siguiente, aunque empiece con '-'. Las
    /// contraseñas generadas incluyen el guion entre sus símbolos, y una que empezaba con él se tomaba como otra
    /// opción: el sobre no se podía abrir con -p usando la contraseña del acta.
    /// </summary>
    private static readonly HashSet<string> SecretValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "-p", "--password", "--sign-cert-password", "--cert-password"
    };

    public CommandParser(string[] args)
    {
        _args = args.ToList();
        Parse();
    }

    public string? Command => _args.Count > 0 ? _args[0] : null;

    private void Parse()
    {
        for (int i = 1; i < _args.Count; i++)
        {
            var arg = _args[i];
            if (arg.StartsWith('-'))
            {
                var key = arg;
                var value = string.Empty;

                // Una contraseña puede empezar con '-', pero no ser otra opción de ForZip: "-p --gen-password" no es
                // una contraseña.
                if (i + 1 < _args.Count
                    && (!_args[i + 1].StartsWith('-')
                        || (SecretValueOptions.Contains(key) && !CliOptions.All.Contains(_args[i + 1]))))
                {
                    value = _args[i + 1];
                    i++;
                }
                else
                {
                    _withoutValue.Add(key);
                }

                _options[key] = value;
                _occurrences.Add((key, value));
            }
            else
            {
                _positionals.Add(arg);
            }
        }
    }

    public bool HasOption(string shortName, string longName) 
        => _options.ContainsKey(shortName) || _options.ContainsKey(longName);

    public string? GetOption(string shortName, string longName)
    {
        if (_options.TryGetValue(shortName, out var val)) return val;
        if (_options.TryGetValue(longName, out val)) return val;
        return null;
    }

    /// <summary>Todos los valores de una opción repetible, por cualquiera de sus dos nombres, en orden.</summary>
    public List<string> GetOptionValues(string shortName, string longName) =>
        _occurrences
            .Where(o => o.Key.Equals(shortName, StringComparison.OrdinalIgnoreCase) || o.Key.Equals(longName, StringComparison.OrdinalIgnoreCase))
            .Select(o => o.Value)
            .ToList();

    /// <summary>Cada opción tal como se escribió, una vez por aparición.</summary>
    public IEnumerable<string> OptionOccurrences => _occurrences.Select(o => o.Key);

    public List<string> Positionals => _positionals;

    /// <summary>Las opciones que vinieron en la línea de comandos, tal como se escribieron.</summary>
    public IEnumerable<string> OptionNames => _options.Keys;

    /// <summary>Las opciones que vinieron sin valor, tal como se escribieron.</summary>
    public IEnumerable<string> OptionsWithoutValue => _withoutValue;
}
