using Renci.SshNet;
using Renci.SshNet.Common;
using System.Text;

namespace puf
{
    internal class Program
    {
        private const string RemotePcIp = "192.168.1.2";
        private const string UserName = "user";
        private const string UserPassword = "password";
        private const short SshPort = 22;

        private static readonly int _sleepTime = TimeSpan.FromMinutes(1).Milliseconds;

        private static void Main(string[] args)
        {
            Run();
        }

        private static void Run()
        {
            while (true)
            {
                // collect data - IsPower from UPS
                // IsPower less than 10%

                // Power off remote PCs
                ShutdownPc();
            }
        }


        private static void ShutdownPc()
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