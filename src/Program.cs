using System;
using LupinFarm.Storage;
using LupinFarm.Ui;

namespace LupinFarm
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--frame") return DevTools.Frame(args);
            if (args.Length > 0 && args[0] == "--sprites") return DevTools.SpriteSheet();

            string err;
            if (!Terminal.Init(out err))
            {
                Console.Error.WriteLine(err);
                return 1;
            }

            string home = Environment.GetEnvironmentVariable("LUPIN_HOME");
            var store = new SaveStore(string.IsNullOrEmpty(home) ? SaveStore.DefaultDir() : home);
            App app = null;
            try
            {
                app = new App(store);
                app.Run();
                return 0;
            }
            catch (Exception e)
            {
                store.LogError(e);
                if (app != null) app.EmergencySave();
                Terminal.Restore();
                Console.Error.WriteLine("Lupin Farm stopped: " + e.Message);
                Console.Error.WriteLine("Details: " + store.ErrorLogPath);
                return 1;
            }
            finally
            {
                Terminal.Restore();
            }
        }
    }
}
