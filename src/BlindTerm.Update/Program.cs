using BlindTerm.Core.Updates;

if (args.Length > 0 && args[0].Equals("--apply-update", StringComparison.OrdinalIgnoreCase))
    args = args[1..];

return UpdateApplier.Run(args);
