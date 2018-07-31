using System;
using System.IO;
using System.IO.Ports;
using System.Configuration;
using System.Configuration.Install;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace uploader
{
    public delegate void LogEventHandler(string message, int level = 0);

    public delegate void ProgressEventHandler(double completed);

    public delegate void MonitorEventHandler(string portname);

    public delegate void UploadEventHandler(string portname, string filename);

    public delegate void QuitEventHandler();

    class MainClass
    {
        public static void Main(string[] args)
        {
            new AppLogic(args);
        }
    }

    class AppLogic
    {
        IHex ihex;
        Uploader upl;
        SerialPort port;
        bool upload_in_progress;
        int logLevel = 1;  // Adjust for more logging output
        string config_name = "SiKUploader";
        System.Configuration.Configuration config;
        ConfigSection config_section;

        public string FileName { get; }
        public string PortName { get; }
        public int BaudRate { get; }
        public string ParamFileName { get; }

        public AppLogic(string[] args)
        {
            ParamFileName = null;
            PortName = null;
            FileName = null;
            BaudRate = 57600;

            if (args.Length == 0)
            {
                displayHelp();
                Environment.Exit(0);
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-port" && args.Length > (i + 1))
                {
                    PortName = args[++i];
                    continue;
                }
                if (args[i] == "-f" && args.Length > (i + 1))
                {
                    FileName = args[++i];
                    continue;
                }
                if (args[i] == "-c" && args.Length > (i + 1))
                {
                    ParamFileName = args[++i];
                    continue;
                }
                if (args[i] == "-b" && args.Length > (i + 1))
                {
                    BaudRate = int.Parse(args[++i]);
                    continue;
                }
                if (args[i] == "-h" && args.Length > (i + 1))
                {
                    displayHelp();
                    Environment.Exit(0);
                }
                Console.WriteLine("Unknown parameter : " + args[i]);
                displayHelp();
                Environment.Exit(0);
            }

            if (PortName == null)
            {
                Console.WriteLine("Com port is not set");
                displayHelp();
                Environment.Exit(0);
            }

            //string[] ports = SerialPort.GetPortNames();

            //ports = ports.Select(p => trimcomportname(p.TrimEnd())).ToArray();

           /** foreach(string port in ports)
            {
                using (SerialPort serialPort = new SerialPort()) {
                    serialPort.PortName = port;
                    serialPort.BaudRate = BaudRate;
                    try
                    {
                        serialPort.Open();
                        if (doConnect(serialPort))
                        {
                            PortName = port;
                        }
                    }
                    catch { };
                }                
            }
           **/

            if (FileName != null)
            {
                UploadFW();
            }


            if (ParamFileName != null)
            {
                UploadParameters();
            }
        }

        private void displayHelp()
        {
            Console.WriteLine("\nUsage : radio-uploader.exe [-f firmware_file] -port com_port [-b baud_rate] [-c config_file]");
            Console.WriteLine("     -f  - path to radio firware file");
            Console.WriteLine("     -port  - com port to connect");
            Console.WriteLine("     -b  - baud rate at com port, if not set default 57600 is used");
            Console.WriteLine("     -c  - path to configuration file ");
            Console.WriteLine("     -h  - display this help ");
            Console.WriteLine("Example: radio-uploader.exe -port COM25 -b 57600 -c gps_rtk.cfg");
        }

        static string trimcomportname(string input)
        {
            var match = Regex.Match(input.ToUpper(), "(COM[0-9]+)");

            if (match.Success)
            {
                return match.Groups[0].Value;
            }

            return input;
        }

        void UploadParameters()
        {
            SerialPort comPort = new SerialPort();

            try
            {
                comPort.PortName = PortName;
                comPort.BaudRate = BaudRate;

                comPort.Open();
            }
            catch
            {
                log("Invalid ComPort or in use " + PortName + ", " + BaudRate);
                return;
            }

            var sr = new StreamReader(ParamFileName);
            var paramDict = new Dictionary<string, string>();

            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine();
                Console.WriteLine(line);
                var name = line.Substring(0, line.IndexOf(':'));
                var value = line.Substring(line.IndexOf('=') + 1);
                if (name == "S0") continue;  // Skip Format id, as its readonly
                paramDict[name] = value;
            }

            if (doConnect(comPort))
            {
                foreach ( KeyValuePair<string, string> pair in paramDict ) {                   
                    var cmd = "AT" + pair.Key + "=" + pair.Value;
                    Console.WriteLine(cmd);
                    doCommand(comPort, cmd);
                }
                doCommand(comPort, "AT&W"); // Save
                doCommand(comPort, "ATZ"); // Reboot
            }

            comPort.Close();
            
        }


      

        private void log(string message, int level = 0)
        {
            // log a message to the console
            if (level <= logLevel)
                Console.WriteLine(message);
        }

        bool upload_xmodem(SerialPort comPort)
        {
            // try xmodem mode
            // xmodem - short cts to ground
            try
            {
                log("Trying XModem Mode");
                //comPort.BaudRate = 57600;
                comPort.BaudRate = BaudRate;
                comPort.ReadTimeout = 1000;

                Thread.Sleep(2000);
                var tempd = comPort.ReadExisting();
                Console.WriteLine(tempd);
                comPort.Write("U");
                Thread.Sleep(1000);
                var resp1 = Serial_ReadLine(comPort); // echo
                var resp2 = Serial_ReadLine(comPort); // echo 2
                var tempd2 = comPort.ReadExisting(); // posibly bootloader info / use to sync
                // identify
                comPort.Write("i");
                // responce is rfd900....
                var resp3 = Serial_ReadLine(comPort); //echo
                var resp4 = Serial_ReadLine(comPort); // newline
                var resp5 = Serial_ReadLine(comPort); // bootloader info
                log(resp5);

            }
            catch (Exception ex2)
            {
                log(ex2.Message);
            }

            return false;
        }

        private string Serial_ReadLine(SerialPort comPort)
        {
            var sb = new StringBuilder();
            var Deadline = DateTime.Now.AddMilliseconds(comPort.ReadTimeout);

            while (DateTime.Now < Deadline)
            {
                if (comPort.BytesToRead > 0)
                {
                    var data = (byte)comPort.ReadByte();
                    sb.Append((char)data);
                    if (data == '\n')
                        break;
                }
            }

            return sb.ToString();
        }

        private void UploadFW()
        {
            SerialPort comPort = new SerialPort();

            var uploader = new Uploader();
           
            try
            {
                comPort.PortName = PortName;
                comPort.BaudRate = 115200;

                comPort.Open();
            }
            catch
            {
                log("Invalid ComPort or in use");
                return;
            }

            uploader.ProgressEvent += progress;
            uploader.LogEvent += log;

            // prep what we are going to upload
            var iHex = new IHex();

            var bootloadermode = false;

            // attempt bootloader mode
            try
            {
          
                comPort.BaudRate = 115200;

                log("Trying Bootloader Mode");

                uploader.port = comPort;
                uploader.connect_and_sync();

                log("In Bootloader Mode");
                bootloadermode = true;
            }
            catch (Exception ex1)
            {
                log(ex1.Message);

                // cleanup bootloader mode fail, and try firmware mode
                comPort.Close();
                if (comPort.IsOpen)
                {
                    // default baud... guess
                    comPort.BaudRate = 57600;
                }
                else
                {
                    comPort.BaudRate = BaudRate;
                }
                try
                {
                    comPort.Open();
                }
                catch
                {
                    log("Error opening port");
                    return;
                }


                log("Trying Firmware Mode");
                bootloadermode = false;
            }

            // check for either already bootloadermode, or if we can do a ATI to ID the firmware 
            if (bootloadermode || doConnect(comPort))
            {
                // put into bootloader mode/update mode
                if (!bootloadermode)
                {
                    try
                    {
                        comPort.Write("AT&UPDATE\r\n");
                        var left = comPort.ReadExisting();
                        log(left);
                        Sleep(700);
                        comPort.BaudRate = 115200;
                    }
                    catch
                    {
                    }

                    comPort.BaudRate = 115200;
                }

                try
                {
                    // force sync after changing baudrate
                    uploader.connect_and_sync();
                }
                catch
                {
                    log("Failed to sync with Radio");
                    goto exit;
                }

                var device = Uploader.Board.FAILED;
                var freq = Uploader.Frequency.FAILED;

                // get the device type and frequency in the bootloader
                uploader.getDevice(ref device, ref freq);


                // load the hex
                try
                {
                    iHex.load(FileName);
                }
                catch
                {
                    log("Bad Firmware File");
                    goto exit;
                }

                // upload the hex and verify
                try
                {
                    uploader.upload(comPort, iHex);
                }
                catch (Exception ex)
                {
                    log("Upload Failed " + ex.Message);
                }

            }
            else
            {
                log("Failed to identify Radio");
            }

            exit:
            if (comPort.IsOpen)
                comPort.Close();
        }

        private void progress(double completed)
        {
            int done = (int)(completed * 100);
            if (done > 0 && 10%done == 0) {
                Console.Write(".");
            }
        }

        public bool doConnect(SerialPort comPort)
        {
            try
            {
                Console.WriteLine("doConnect");

                var trys = 1;

                // setup a known enviroment
                comPort.Write("ATO\r\n");

                retry:

                // wait
                Sleep(1500, comPort);
                comPort.DiscardInBuffer();
                // send config string
                comPort.Write("+");
                Sleep(200, comPort);
                comPort.Write("+");
                Sleep(200, comPort);
                comPort.Write("+");
                Sleep(1500, comPort);
                // check for config response "OK"
                log("Connect btr " + comPort.BytesToRead + " baud " + comPort.BaudRate);
                // allow time for data/response

                if (comPort.BytesToRead == 0 && trys <= 3)
                {
                    trys++;
                    log("doConnect retry");
                    goto retry;
                }

                var buffer = new byte[20];
                var len = comPort.Read(buffer, 0, buffer.Length);
                var conn = Encoding.ASCII.GetString(buffer, 0, len);
                log("Connect first response " + conn.Replace('\0', ' ') + " " + conn.Length);
                if (conn.Contains("OK"))
                {
                    //return true;
                }
                else
                {
                    // cleanup incase we are already in cmd mode
                    comPort.Write("\r\n");
                }

                doCommand(comPort, "AT&T");

                var version = doCommand(comPort, "ATI");

                log("Connect Version: " + version.Trim() + "\n");

                var regex = new Regex(@"SiK\s+(.*)\s+on\s+(.*)");

                if (regex.IsMatch(version))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }


        public string doCommand(SerialPort comPort, string cmd, bool multiLineResponce = false, int level = 0)
        {
            if (!comPort.IsOpen)
                return "";

            comPort.DiscardInBuffer();

            log("Doing Command " + cmd);

            comPort.Write(cmd + "\r\n");

            comPort.ReadTimeout = 1000;

            // command echo
            var cmdecho = Serial_ReadLine(comPort);

            if (cmdecho.Contains(cmd))
            {
                var value = "";

                if (multiLineResponce)
                {
                    var deadline = DateTime.Now.AddMilliseconds(1000);
                    while (comPort.BytesToRead > 0 || DateTime.Now < deadline)
                    {
                        try
                        {
                            value = value + Serial_ReadLine(comPort);
                        }
                        catch
                        {
                            value = value + comPort.ReadExisting();
                        }
                    }
                }
                else
                {
                    value = Serial_ReadLine(comPort);

                    if (value == "" && level == 0)
                    {
                        return doCommand(comPort, cmd, multiLineResponce, 1);
                    }
                }

                log(value.Replace('\0', ' '));

                return value;
            }

            comPort.DiscardInBuffer();

            // try again
            if (level == 0)
                return doCommand(comPort, cmd, multiLineResponce, 1);

            return "";
        }

        private void Sleep(int mstimeout, SerialPort comPort = null)
        {
            var endtime = DateTime.Now.AddMilliseconds(mstimeout);

            while (DateTime.Now < endtime)
            {
                Thread.Sleep(1);

                // prime the mavlinkserial loop with data.
                if (comPort != null)
                {
                    var test = comPort.BytesToRead;
                    test++;
                }
            }
        }
    }



    public sealed class ConfigSection : ConfigurationSection
    {
        public ConfigSection()
        {
        }

        [ConfigurationProperty("lastPath", DefaultValue = "")]
        public string lastPath
        {
            get
            {
                return (string)this["lastPath"];
            }
            set
            {
                this["lastPath"] = value;
            }
        }

        [ConfigurationProperty("lastPort", DefaultValue = "")]
        public string lastPort
        {
            get
            {
                return (string)this["lastPort"];
            }
            set
            {
                this["lastPort"] = value;
            }
        }
    }

}
