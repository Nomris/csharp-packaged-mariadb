using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Org.Websn.Utility;

using SharpCompress;

namespace TestingApp0
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Process.GetProcessesByName("mariadbd").ForEach(proc => proc.Kill());

            Console.WriteLine("Wating for DEBUGGER to attache");
            while (!Debugger.IsAttached) Thread.Sleep(250);

            MariaDbPackager.RuntimeConfig runCfg = MariaDbPackager.EnsureServerAsync(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "managed")).Result;

            Debugger.Break();

            Process server = runCfg.Start();

            IDbConnection connection = new MySqlConnector.MySqlConnection(runCfg.ConnectionString);
            connection.Open();

            using (IDbCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = "CREATE DATABASE _test__" + DateTime.UtcNow.Millisecond.ToString();
                cmd.ExecuteNonQuery();
            }

            Debugger.Break();

            runCfg.Stop();

            Console.WriteLine("Wating for DEBUGGER to dettach");
            while (Debugger.IsAttached) Thread.Sleep(250);

        }
    }
}
