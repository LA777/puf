using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using puf.Models;
using Renci.SshNet;
using Renci.SshNet.Common;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace puf
{
    internal class Program
    {
        private const string RemotePcIp = "192.168.137.239";
        private const string UserName = "user";
        private const string UserPassword = "password";
        private const short SshPort = 22;
        private const short SensorMonitorPort = 11122;

        private static ILogger _logger;

        private static readonly int SleepTime = 60 * 60 * 1000;
        private static readonly HttpClient HttpClient = new(); // TODO LA - refactor

        private static IConfiguration Configuration { get; } = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, @"settings\settings.json"), false, false)
            .Build();

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions<ApplicationSettings>()
                .Bind(Configuration)
                .ValidateDataAnnotations();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null)
            {
                throw new ArgumentException("Version is null.", nameof(Version));
            }

            var applicationSettings = Configuration.Get<ApplicationSettings>();
            if (applicationSettings == null)
            {
                throw new ArgumentException("ApplicationSettings is null.", nameof(ApplicationSettings));
            }

            if (applicationSettings.LogFilePath == null)
            {
                throw new ArgumentException("LogFilePath in ApplicationSettings is null.");
            }

            var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.WithProperty("Version", version)
                .WriteTo.Console(LogEventLevel.Information, "{Timestamp:yyyy-MM-dd HH:mm:ss} | {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(Path.Combine(AppContext.BaseDirectory, applicationSettings.LogFilePath), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            services.AddLogging(loggingBuilder => { loggingBuilder.AddSerilog(logger, true); });
        }

        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();
            _logger = serviceProvider.GetService<ILogger>()!;

            _logger.Information("Start App.");

            try
            {
                Run();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                throw;
            }
            finally
            {
                HttpClient.Dispose();
            }

            _logger.Information("Exit App.");
        }

        private static void Run()
        {
            HttpClient.Timeout = TimeSpan.FromSeconds(30);

            while (true)
            {
                // collect data - IsPower from UPS
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, $"http://{RemotePcIp}:{SensorMonitorPort}");
                HttpResponseMessage result;

                try
                {
                    result = HttpClient.Send(httpRequestMessage);
                }
                catch (Exception exception)
                {
                    _logger.Debug(exception.Message);
                    Thread.Sleep(SleepTime);
                    continue;
                }

                _logger.Information($"Status code: {result.StatusCode}");
                var json = result.Content.ReadAsStringAsync().Result;
                var sensorItems = JsonSerializer.Deserialize<IList<SensorItem>>(json);
                //var upsSensor1 = sensorItems?.Where(x => x.SensorApp == "HWiNFO" && x.SensorClass == "UPS");
                //if (upsSensor1 != null)
                //{
                //    foreach (var upsSensor in upsSensor1)
                //    {
                //        Console.WriteLine($"SensorApp: {upsSensor.SensorApp}");
                //        Console.WriteLine($"SensorClass: {upsSensor.SensorClass}");
                //        Console.WriteLine($"SensorName: {upsSensor.SensorName}");
                //        Console.WriteLine($"SensorValue: {upsSensor.SensorValue}");
                //        Console.WriteLine($"SensorUnit: {upsSensor.SensorUnit}");
                //        Console.WriteLine($"SensorUpdateTime: {upsSensor.SensorUpdateTime}");
                //    }
                //}


                // IsPower less than 10%
                var sensorItemChargeLevel = sensorItems?.FirstOrDefault(x => x.SensorApp == "HWiNFO" && x.SensorClass == "UPS" && x.SensorName == "Charge Level");
                var chargeLevelString = sensorItemChargeLevel?.SensorValue;
                var chargeLevel = Convert.ToInt32(chargeLevelString);

                // Is AC Power - NO
                // Is Charging - NO
                // Is Discharging - Yes
                var sensorItemAcPower = sensorItems?.FirstOrDefault(x => x.SensorApp == "HWiNFO" && x.SensorClass == "UPS" && x.SensorName == "AC Power");
                var acPower = sensorItemAcPower?.SensorValue;

                var sensorItemCharging = sensorItems?.FirstOrDefault(x => x.SensorApp == "HWiNFO" && x.SensorClass == "UPS" && x.SensorName == "Charging");
                var charging = sensorItemCharging?.SensorValue;

                var sensorItemDischarging = sensorItems?.FirstOrDefault(x => x.SensorApp == "HWiNFO" && x.SensorClass == "UPS" && x.SensorName == "Discharging");
                var discharging = sensorItemDischarging?.SensorValue;

                _logger.Information($"AC Power: {acPower}");
                _logger.Information($"Charging: {charging}");
                _logger.Information($"Discharging: {discharging}");

                if (acPower == "0" && charging == "0" && discharging == "1")
                {
                    if (chargeLevel < 20)
                    {
                        _logger.Information("Shutdown PC!"); // TODO LA - Log to file
                        ShutdownPcCmdFake();
                    }
                }

                Thread.Sleep(SleepTime);

                //LockPcCmd();
                //Console.WriteLine("Press Ctrl-C to exit...");
                //var key = Console.ReadKey();
                //if (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0)
                //{
                //    Console.WriteLine("Exit!");
                //    return;
                //}
            }
        }

        private static void LockPcCmd()
        {
            var cmd = new Process();

            cmd.StartInfo.FileName = "cmd.exe";
            cmd.StartInfo.RedirectStandardInput = true;
            cmd.StartInfo.RedirectStandardOutput = true;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.StartInfo.UseShellExecute = false;

            cmd.Start();

            /* execute "dir" */

            cmd.StandardInput.WriteLine("Rundll32.exe user32.dll,LockWorkStation");
            cmd.StandardInput.Flush();
            cmd.StandardInput.Close();
            Console.WriteLine(cmd.StandardOutput.ReadToEnd());
        }

        private static void ShutdownPcCmdFake()
        {
            _logger.Information("ShutdownPcCmdFake");
        }

        private static void ShutdownPcCmd()
        {
            _logger.Information("Shutdown PC!");

            var cmd = new Process();

            cmd.StartInfo.FileName = "cmd.exe";
            cmd.StartInfo.RedirectStandardInput = true;
            cmd.StartInfo.RedirectStandardOutput = true;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.StartInfo.UseShellExecute = false;

            cmd.Start();

            /* execute "dir" */

            cmd.StandardInput.WriteLine("shutdown /s");
            cmd.StandardInput.Flush();
            cmd.StandardInput.Close();
            _logger.Information(cmd.StandardOutput.ReadToEnd());
        }


        private static void ShutdownPcSsh()
        {
            Console.WriteLine("Shutdown PC...");
            using var sshClient = new SshClient(RemotePcIp, SshPort, UserName, UserPassword);
            sshClient.ErrorOccurred += SshClientErrorOccurred;

            try
            {
                sshClient.Connect();
                Console.WriteLine(sshClient.ConnectionInfo.ServerVersion);
                var modes = new Dictionary<TerminalModes, uint>();
                using var stream = sshClient.CreateShellStream("xterm", 255, 50, 800, 600, 1024, modes);
                stream.DataReceived += DataReceived;

                stream.WriteLine("shutdown /s");

                Console.WriteLine("PC shutdown completed.");
                sshClient.Disconnect();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private static void SshClientErrorOccurred(object? sender, ExceptionEventArgs exceptionEventArgs)
        {
            Console.WriteLine("An ssh error occurred:");
            Console.WriteLine(exceptionEventArgs.Exception.Message);
        }

        private static void DataReceived(object? sender, ShellDataEventArgs shellDataEventArgs)
        {
            Console.WriteLine("DataReceived:");
            Console.WriteLine(shellDataEventArgs.Line);
            Console.WriteLine($"text: {Encoding.UTF8.GetString(shellDataEventArgs.Data, 0, shellDataEventArgs.Data.Length)}");
        }
    }
}