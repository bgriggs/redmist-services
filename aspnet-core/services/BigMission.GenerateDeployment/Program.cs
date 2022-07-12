using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BigMission.GenerateDeployment
{
    class Program
    {
        const string SERVICE_ROOT_DIR = "services";
        const string VM_IP = "142.93.123.108";
        const string VM_USER = "root";
        const string VM_PASS = "5@2f2*Xz67kADgr";
        const string CMD = "Invoke-SshCommand -Command \"{0}\" -SessionId $sid.SessionId";

        static void Main(string[] args)
        {
            var wd = Directory.GetCurrentDirectory();
            var index = wd.IndexOf(SERVICE_ROOT_DIR);
            var serviceRoot = wd.Substring(0, index + SERVICE_ROOT_DIR.Length);
            Console.WriteLine(serviceRoot);
            var serviceDirs = Directory.GetDirectories(serviceRoot, "BigMission.*");

            var systemdFiles = new List<string>();
            foreach (var d in serviceDirs)
            {
                var serviceFiles = Directory.GetFiles(d, "*.service");
                systemdFiles.AddRange(serviceFiles);
            }

            var sb = new StringBuilder();
            Header(sb);

            foreach (var sf in systemdFiles)
            {
                var fi = new FileInfo(sf);
                var serviceText = File.ReadAllText(sf);
                serviceText = serviceText.Replace("\r\n", "\\n");
                serviceText = $"printf \"\"{serviceText}\"\" > /lib/systemd/system/{fi.Name}";
                sb.AppendLine(string.Format(CMD, serviceText));
                sb.AppendLine(string.Format(CMD, $"sudo systemctl enable /lib/systemd/system/{fi.Name}"));
            }
            sb.AppendLine(string.Format(CMD, "sudo systemctl daemon-reload"));

            File.WriteAllText("servicedeploy.ps1", sb.ToString());
            Console.WriteLine("Finished");
        }

        static void Header(StringBuilder sb)
        {
            sb.AppendLine("#Install-Module -Name Posh-SSH");
            sb.AppendLine(string.Format("$Password = \"{0}\"", VM_PASS));
            sb.AppendLine(string.Format("$User = \"{0}\"", VM_USER));
            sb.AppendLine(string.Format("$ComputerName = \"{0}\"", VM_IP));
            sb.AppendLine();
            sb.AppendLine("$secpasswd = ConvertTo-SecureString $Password -AsPlainText -Force");
            sb.AppendLine("$Credentials = New-Object System.Management.Automation.PSCredential($User, $secpasswd)");
            sb.AppendLine("$sid = New-SSHSession -ComputerName $ComputerName -Credential $Credentials");
            sb.AppendLine();

        }
    }
}
