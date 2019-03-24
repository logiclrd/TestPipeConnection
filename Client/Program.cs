using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
	public class Program
	{
		static void Main(string[] args)
		{
			// This "debugger" path exists to allow me to debug into the sequence of statements that NamedPipeClientStream uses in its Connect()
			// method. I duplicated these steps into ClientConnector.cs explicitly for this purpose. This was used to confirm that the error
			// that is triggered by the as-yet-unknown cause is an ERROR_SEM_TIMEOUT from WaitNamedPipe().
			if ((args.Length > 0) && (args[0] == "debugger"))
				new ClientConnector().Connect(10000);

			// Connect to the test server.
			NamedPipeClientStream clientStream = null;

			try
			{
				clientStream = new NamedPipeClientStream(".", "Test Pipe", PipeDirection.InOut, PipeOptions.Asynchronous);
				clientStream.Connect(10000);
			}
			catch (Exception e)
			{
				if (args.Length > 0)
				{
					// If our command-line isn't empty, don't permit the console window to close before exception details have been seen.
					Console.WriteLine(e);
					Console.ReadLine();
				}

				Environment.Exit(1);
			}

			try
			{
				Console.WriteLine("Connected");
				Console.WriteLine("=========");

				// In early testing, I was curious to see whether clients sending information to the server had any impact on the incidence.
				// This Task is a remnant of that testing, a simple pump that sends any line of text entered into the console to the server.
				/*
				var t1 = Task.Run(
					() =>
					{
						using (var writer = new StreamWriter(clientStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
						{
							while (true)
							{
								string line = Console.ReadLine();

								if (line == null)
									break;

								writer.WriteLine(line);
								writer.Flush();
							}
						}
					});
				 */

				// Pump message lines received from the pipe server. The pipe is operating in byte mode, so we just need to read bytes until we get an end-of-line
				// sequence. StreamReader will do that for us.
				using (var reader = new StreamReader(clientStream))
				{
					string preamble = reader.ReadLine();

					Console.WriteLine(preamble);

					// Verify that we get the expected protocol, and notify the caller if the messages we received are garbled or otherwise incorrect.
					if (!preamble.StartsWith("You are connected to "))
						Environment.Exit(2);

					bool haveEndOfStream = false;
					int numberOfWords = 0;

					while (!haveEndOfStream)
					{
						string line = reader.ReadLine();

						if (line == null)
							break;

						Console.WriteLine(line);

						haveEndOfStream |= (line == "END OF STREAM");
						if (line.StartsWith("WORD: "))
							numberOfWords++;
					}

					// Additional protocol validation.
					if (!haveEndOfStream)
						Environment.Exit(3);

					if (numberOfWords != 10)
						Environment.Exit(4);

					// Expected result: we received "You are connected to", then 10 words prefixed with "WORD:", then "END OF STREAM".
					Environment.Exit(0);
				}
			}
			catch (Exception e)
			{
				if (args.Length > 0)
				{
					// If our command-line isn't empty, don't permit the console window to close before exception details have been seen.
					Console.WriteLine(e);
					Console.ReadLine();
				}

				Environment.Exit(5);
			}
		}
	}
}
