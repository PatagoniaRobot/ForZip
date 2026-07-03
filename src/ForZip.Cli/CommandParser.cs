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

                if (i + 1 < _args.Count && !_args[i + 1].StartsWith('-'))
                {
                    value = _args[i + 1];
                    i++;
                }

                _options[key] = value;
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

    public List<string> Positionals => _positionals;
}
