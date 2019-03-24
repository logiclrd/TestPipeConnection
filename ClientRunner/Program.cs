using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ClientRunner
{
	class Program
	{
		static void Main(string[] args)
		{
			const int NumChildren = 100;

			// Run NumChildren concurrent child processes as fast as we can, and then wait for each one to finish, inspecting
			// its exit code to make sure it saw the expected protocol from the server. The waiting is synchronous and ordered,
			// but that's okay; if the processes exit out-of-order, their exit codes will wait patiently to be collected.
			List<Process> children = new List<Process>();

			for (int i = 0; i < NumChildren; i++)
				children.Add(LaunchClient());

			string commonCheckResult = null;

			for (int i = 0; i < NumChildren; i++)
			{
				var checkResult = CheckClient(children[i]);

				if (commonCheckResult == null)
					commonCheckResult = checkResult;
				else if (checkResult != commonCheckResult)
					commonCheckResult = "";

				Console.WriteLine("{0,4}: {1}", i + 1, checkResult);
			}

			if (commonCheckResult.Length > 0)
			{
				Console.WriteLine();
				Console.WriteLine("All check results are \"{0}\"", commonCheckResult);
			}

			if (Debugger.IsAttached)
				Console.ReadLine();
		}

		static Process LaunchClient()
		{
			return Process.Start(typeof(Client.Program).Assembly.Location);
		}

		static string CheckClient(Process childProcess)
		{
			childProcess.WaitForExit();

			switch (childProcess.ExitCode)
			{
				case 0: return "succeeded";
				case 1: return "FAILED TO CONNECT";
				case 2: return "NO PREAMBLE";
				case 3: return "NO END OF STREAM";
				case 4: return "WRONG NUMBER OF RECORDS";
				case 5: return "EXCEPTION WHILE READING";

				default: return "UNKNOWN";
			}
		}
	}
}
