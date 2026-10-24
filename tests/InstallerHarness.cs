using System;

// This is the only executable the offline tests launch. Game fixture bytes are never executed.
public static class InstallerHarness
{
    public static int Main(string[] args)
    {
        try
        {
            InstallerEngine.PayloadPath = args[2];
            Action<int, string> progress = delegate(int percent, string message)
            {
                if (args[0] == "interrupt" && percent >= 40 && percent < 100)
                    throw new InvalidOperationException("Injected failure after a target write");
            };
            if (args[0] == "uninstall") InstallerEngine.Uninstall(args[1], progress);
            else InstallerEngine.Install(args[1], progress);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 2;
        }
    }
}
