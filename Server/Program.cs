using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
	class Program
	{
		static readonly string[] Words = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "Words.txt"));

		static object s_sync = new object();

		static void Main(string[] args)
		{
			BeginWaitForNextConnection();

			Console.ReadLine();
		}

		static void BeginWaitForNextConnection()
		{
			NamedPipeServerStream serverStream = new NamedPipeServerStream("Test Pipe", PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

			serverStream.BeginWaitForConnection(ConnectCallback, serverStream);
		}

		private static PipeSecurity CreateAllAccessPipeSecurity()
		{
			PipeSecurity allAccess = new PipeSecurity();

			allAccess.AddAccessRule(new PipeAccessRule(
				new SecurityIdentifier(WellKnownSidType.WorldSid, null),
				PipeAccessRights.FullControl,
				AccessControlType.Allow));

			return allAccess;
		}

		[DllImport("kernel32")]
		static extern bool GetNamedPipeClientProcessId(SafePipeHandle Pipe, out int ClientProcessId);

		static void ConnectCallback(IAsyncResult result)
		{
			// Basic protocol currently implemented here:
			// => Client connects
			// => All information passed over the pipe is sent by the server to the client
			// => The server tells the client its thread ID. This simply allows us to be casually certain that every client is getting unique treatment.
			// => The server sends 10 random words to the client.
			// => The server tells the client it has reached the end of the stream.
			// => The server stream is closed.

			var serverStream = (NamedPipeServerStream)result.AsyncState;

			serverStream.EndWaitForConnection(result);

			BeginWaitForNextConnection();

			GetNamedPipeClientProcessId(serverStream.SafePipeHandle, out int clientProcessID);

			Console.WriteLine("Connected a client from process ID {0}", clientProcessID);

			var t1 = Task.Run(
				() =>
				{
					int tid = Thread.CurrentThread.ManagedThreadId;

					try
					{
						Random rnd = new Random();

						using (var writer = new StreamWriter(serverStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
						{
							void Emit(string line)
							{
								writer.WriteLine(line);
								writer.Flush();

								lock (s_sync)
									Console.WriteLine("[{0}] {1}", tid, line);
							}

							Emit("You are connected to thread " + tid);

							for (int i = 0; i < 10; i++)
							{
								Emit("WORD: " + Words[rnd.Next(Words.Length)]);

								Thread.Sleep(rnd.Next(250, 2500));
							}

							Emit("END OF STREAM");
						}

						serverStream.Close();
					}
					catch (IOException)
					{
						lock (s_sync)
							Console.WriteLine("[{0}] DISCONNECTED", tid);
					}
				});

			// There is a corresponding task in the client that is also commented out.
			/*
			var t2 = Task.Run(
				() =>
				{
					var decoder = new UTF8Encoding();

					byte[] readBuffer = new byte[1];

					try
					{
						while (true)
						{
							int numRead = serverStream.Read(readBuffer, 0, 1);

							if (numRead < 0)
								break;

							if (numRead > 0)
							{
								char[] chars = decoder.GetChars(readBuffer);

								lock (s_sync)
								{
									Console.ForegroundColor = ConsoleColor.Cyan;

									for (int i = 0; i < chars.Length; i++)
										Console.Write(chars[i]);

									Console.ForegroundColor = ConsoleColor.Gray;
								}
							}
						}
					}
					catch (EndOfStreamException) { }
				});
			 */
		}
	}
}
