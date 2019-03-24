using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading;

using Microsoft.Win32.SafeHandles;

namespace Client
{
	// This code is reconstructed by taking just the parts related to Connect() from NamedPipeClientStream in Reference Source.
	// Its purpose is to allow debugger insight into the exact steps during connection, so that the error returned when the
	// problem is being reproduced can be pinpointed.
	//
	// It was used for this purpose, revealing that the error is ERROR_SEM_TIMEOUT returned by WaitNamedPipe.
	class ClientConnector
	{
		[DllImport("KERNEL32", EntryPoint = "CreateFile", CharSet = CharSet.Auto, SetLastError = true, BestFitMapping = false)]
		[SecurityCritical]
		internal static extern SafePipeHandle CreateNamedPipeClient(String lpFileName,
				int dwDesiredAccess, System.IO.FileShare dwShareMode,
				SECURITY_ATTRIBUTES securityAttrs, System.IO.FileMode dwCreationDisposition,
				int dwFlagsAndAttributes, IntPtr hTemplateFile);

		[DllImport("KERNEL32", SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
		[SecurityCritical]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool WaitNamedPipe(String name, int timeout);

		[StructLayout(LayoutKind.Sequential)]
		internal unsafe class SECURITY_ATTRIBUTES
		{
			internal int nLength;
			[SecurityCritical]
			internal unsafe byte* pSecurityDescriptor;
			internal int bInheritHandle;
		}

		HandleInheritability m_inheritability = HandleInheritability.None;
		PipeOptions m_pipeOptions = PipeOptions.Asynchronous;
		TokenImpersonationLevel m_impersonationLevel = TokenImpersonationLevel.None;
		string serverName = ".";
		string pipeName = "Test Pipe";

		internal const int SECURITY_SQOS_PRESENT = 0x00100000;

		internal const int ERROR_SUCCESS = 0x0;
		internal const int ERROR_FILE_NOT_FOUND = 0x2;
		internal const int ERROR_PIPE_BUSY = 0xE7;  // 231

		public void Connect(int timeout)
		{
			if (timeout < 0 && timeout != Timeout.Infinite)
			{
				throw new ArgumentOutOfRangeException("timeout");
			}

			SECURITY_ATTRIBUTES secAttrs = PipeStream_GetSecAttrs(m_inheritability);

			int _pipeFlags = (int)m_pipeOptions;
			
			if (m_impersonationLevel != TokenImpersonationLevel.None)
			{
				_pipeFlags |= SECURITY_SQOS_PRESENT;
				_pipeFlags |= (((int)m_impersonationLevel - 1) << 16);
			}

			string m_normalizedPipePath = Path.GetFullPath(@"\\" + serverName + @"\pipe\" + pipeName); ;

			// This is the main connection loop. It will loop until the timeout expires.  Most of the 
			// time, we will be waiting in the WaitNamedPipe win32 blocking function; however, there are
			// cases when we will need to loop: 1) The server is not created (WaitNamedPipe returns 
			// straight away in such cases), and 2) when another client connects to our server in between 
			// our WaitNamedPipe and CreateFile calls.
			int startTime = Environment.TickCount;
			int elapsed = 0;
			var sw = new SpinWait();
			do
			{
				// Wait for pipe to become free (this will block unless the pipe does not exist).
				if (!WaitNamedPipe(m_normalizedPipePath, timeout - elapsed))
				{
					int errorCode = Marshal.GetLastWin32Error();

					// Server is not yet created so let's keep looping.
					if (errorCode == ERROR_FILE_NOT_FOUND)
					{
						sw.SpinOnce();
						continue;
					}

					// The timeout has expired.
					if (errorCode == ERROR_SUCCESS)
					{
						break;
					}

					throw new Exception("Win32 error " + errorCode);
				}

				const int GENERIC_READ = unchecked((int)0x80000000);
				const int GENERIC_WRITE = 0x40000000;

				int m_access = GENERIC_READ | GENERIC_WRITE;

				// Pipe server should be free.  Let's try to connect to it.
				SafePipeHandle handle = CreateNamedPipeClient(m_normalizedPipePath,
																		m_access,           // read and write access
																		0,                  // sharing: none
																		secAttrs,           // security attributes
																		FileMode.Open,      // open existing 
																		_pipeFlags,         // impersonation flags
																		IntPtr.Zero);  // template file: null

				if (handle.IsInvalid)
				{
					int errorCode = Marshal.GetLastWin32Error();

					// Handle the possible race condition of someone else connecting to the server 
					// between our calls to WaitNamedPipe & CreateFile.
					if (errorCode == ERROR_PIPE_BUSY)
					{
						sw.SpinOnce();
						continue;
					}

					throw new Exception("Win32 error " + errorCode);
				}

				// Success! 
				//InitializeHandle(handle, false, (m_pipeOptions & PipeOptions.Asynchronous) != 0);
				//State = PipeState.Connected;

				return;
			}
			while (timeout == Timeout.Infinite || (elapsed = unchecked(Environment.TickCount - startTime)) < timeout);
			// BUGBUG: SerialPort does not use unchecked arithmetic when calculating elapsed times.  This is needed
			//         because Environment.TickCount can overflow (though only every 49.7 days).

			throw new TimeoutException();
		}

		private SECURITY_ATTRIBUTES PipeStream_GetSecAttrs(HandleInheritability inheritability)
		{
			SECURITY_ATTRIBUTES secAttrs = null;
			if ((inheritability & HandleInheritability.Inheritable) != 0)
			{
				secAttrs = new SECURITY_ATTRIBUTES();
				secAttrs.nLength = (int)Marshal.SizeOf(secAttrs);
				secAttrs.bInheritHandle = 1;
			}
			return secAttrs;
		}
	}
}
