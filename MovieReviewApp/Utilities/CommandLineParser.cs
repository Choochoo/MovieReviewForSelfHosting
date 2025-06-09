using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Utilities
{
    public class CommandLineArgs
    {
        public string? InstanceName { get; set; }
        public int? Port { get; set; }
        public bool ShowHelp { get; set; }
        public bool ListInstances { get; set; }
    }

    public static class CommandLineParser
    {
        public static CommandLineArgs Parse(string[] args)
        {
            CommandLineArgs result = new CommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLower();

                switch (arg)
                {
                    case "--instance":
                    case "-i":
                        if (i + 1 < args.Length)
                        {
                            result.InstanceName = InstanceManager.SanitizeInstanceName(args[i + 1]);
                            i++; // Skip next argument since we consumed it
                        }
                        break;

                    case "--port":
                    case "-p":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int port))
                        {
                            result.Port = port;
                            i++; // Skip next argument since we consumed it
                        }
                        break;

                    case "--help":
                    case "-h":
                    case "/?":
                        result.ShowHelp = true;
                        break;

                    case "--list":
                    case "-l":
                        result.ListInstances = true;
                        break;
                }
            }

            return result;
        }

        public static void ShowHelp()
        {
            Console.WriteLine("Movie Review App - Instance Manager");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --instance, -i <name>    Specify instance name (e.g., Family-Movies, Work-Film-Club)");
            Console.WriteLine("  --port, -p <port>        Specify port number (default: auto-assigned)");
            Console.WriteLine("  --list, -l               List all existing instances");
            Console.WriteLine("  --help, -h               Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run --instance Family-Movies --port 5000");
            Console.WriteLine("  dotnet run --instance Work-Film-Club --port 5001");
            Console.WriteLine("  dotnet run --instance Friends-Cinema --port 5002");
            Console.WriteLine("  dotnet run --list");
            Console.WriteLine();
            Console.WriteLine("Instance Storage:");
            Console.WriteLine($"  {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MovieReviewApp", "instances")}");
        }

        public static void ListInstances()
        {
            InstanceManager instanceManager = new InstanceManager();
            List<string> instances = instanceManager.GetAllInstances();

            Console.WriteLine("Existing Movie Review App Instances:");
            Console.WriteLine();

            if (!instances.Any())
            {
                Console.WriteLine("  No instances found. Run the app to create your first instance:");
                Console.WriteLine("    dotnet run --instance \"My-First-Instance\"");
                return;
            }

            foreach (var instanceName in instances)
            {
                try
                {
                    InstanceManager tempInstanceManager = new InstanceManager(instanceName);
                    dynamic config = tempInstanceManager.GetInstanceConfig();
                    
                    Console.WriteLine($"  📁 {instanceName}");
                    Console.WriteLine($"     Display Name: {config.DisplayName}");
                    Console.WriteLine($"     Environment: {config.Environment}");
                    Console.WriteLine($"     Port: {config.Port}");
                    Console.WriteLine($"     Created: {config.CreatedDate:yyyy-MM-dd HH:mm}");
                    Console.WriteLine($"     Last Used: {config.LastUsed:yyyy-MM-dd HH:mm}");
                    
                    if (!string.IsNullOrEmpty(config.Description))
                        Console.WriteLine($"     Description: {config.Description}");
                    
                    Console.WriteLine();
                }
                catch
                {
                    Console.WriteLine($"  📁 {instanceName} (configuration error)");
                    Console.WriteLine();
                }
            }

            Console.WriteLine("To run a specific instance:");
            Console.WriteLine($"  dotnet run --instance <name> --port <port>");
        }

        public static string? SelectInstanceInteractively()
        {
            InstanceManager instanceManager = new InstanceManager();
            List<string> instances = instanceManager.GetAllInstances();

            Console.WriteLine("🎬 Movie Review App - Instance Selector");
            Console.WriteLine();

            if (!instances.Any())
            {
                Console.WriteLine("No existing instances found.");
                Console.WriteLine("Creating a new instance...");
                Console.WriteLine();
                Console.Write("Enter instance name (or press Enter for 'Default'): ");
                string? input = Console.ReadLine();
                return string.IsNullOrWhiteSpace(input) ? "Default" : InstanceManager.SanitizeInstanceName(input.Trim());
            }

            Console.WriteLine("Available instances:");
            for (int i = 0; i < instances.Count; i++)
            {
                try
                {
                    InstanceManager tempInstanceManager = new InstanceManager(instances[i]);
                    dynamic config = tempInstanceManager.GetInstanceConfig();
                    Console.WriteLine($"  {i + 1}. {instances[i]} - {config.DisplayName} ({config.Environment})");
                }
                catch
                {
                    Console.WriteLine($"  {i + 1}. {instances[i]} (configuration error)");
                }
            }

            Console.WriteLine($"  {instances.Count + 1}. Create new instance");
            Console.WriteLine();
            Console.Write($"Select instance (1-{instances.Count + 1}): ");

            string? selection = Console.ReadLine();
            if (int.TryParse(selection, out int index))
            {
                if (index >= 1 && index <= instances.Count)
                {
                    return instances[index - 1];
                }
                else if (index == instances.Count + 1)
                {
                    Console.Write("Enter new instance name: ");
                    string? newName = Console.ReadLine();
                    return string.IsNullOrWhiteSpace(newName) ? "Default" : InstanceManager.SanitizeInstanceName(newName.Trim());
                }
            }

            Console.WriteLine("Invalid selection. Using 'Default' instance.");
            return "Default";
        }
    }
}