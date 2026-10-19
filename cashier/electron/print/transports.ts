import { execFile } from 'node:child_process';
import { appendFile, mkdtemp, rm, writeFile } from 'node:fs/promises';
import net from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { PrintSettings } from '@shared/types';

/**
 * Raw byte delivery to the receipt printer.
 *  - windows: an installed Windows printer by name. Bytes go through the print spooler with the RAW
 *    datatype (winspool.drv OpenPrinter/StartDocPrinter/WritePrinter) so the driver does not
 *    re-render them. Done with a short PowerShell + Add-Type C# snippet: no native node module.
 *  - tcp: network printer, raw socket on port 9100.
 *  - serial: COM port via System.IO.Ports.SerialPort (PowerShell), also without a native module.
 *  - file: virtual transport that appends the bytes to a file (diagnostics / tests).
 */

const RAW_SPOOLER_PS = String.raw`
param([string]$Printer, [string]$File)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class RawPrint {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public class DOCINFO { [MarshalAs(UnmanagedType.LPWStr)] public string pDocName; [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile; [MarshalAs(UnmanagedType.LPWStr)] public string pDataType; }
  [DllImport("winspool.drv", EntryPoint="OpenPrinterW", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool OpenPrinter(string n, out IntPtr h, IntPtr d);
  [DllImport("winspool.drv", SetLastError=true)] public static extern bool ClosePrinter(IntPtr h);
  [DllImport("winspool.drv", EntryPoint="StartDocPrinterW", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool StartDocPrinter(IntPtr h, int level, [In] DOCINFO di);
  [DllImport("winspool.drv", SetLastError=true)] public static extern bool EndDocPrinter(IntPtr h);
  [DllImport("winspool.drv", SetLastError=true)] public static extern bool StartPagePrinter(IntPtr h);
  [DllImport("winspool.drv", SetLastError=true)] public static extern bool EndPagePrinter(IntPtr h);
  [DllImport("winspool.drv", SetLastError=true)] public static extern bool WritePrinter(IntPtr h, byte[] b, int c, out int w);
  public static void Send(string printer, byte[] data) {
    IntPtr h;
    if (!OpenPrinter(printer, out h, IntPtr.Zero)) throw new Exception("OpenPrinter failed: " + Marshal.GetLastWin32Error());
    try {
      var di = new DOCINFO { pDocName = "ShelfGuard receipt", pDataType = "RAW" };
      if (!StartDocPrinter(h, 1, di)) throw new Exception("StartDocPrinter failed: " + Marshal.GetLastWin32Error());
      try {
        StartPagePrinter(h);
        int written;
        if (!WritePrinter(h, data, data.Length, out written) || written != data.Length) throw new Exception("WritePrinter failed: " + Marshal.GetLastWin32Error());
        EndPagePrinter(h);
      } finally { EndDocPrinter(h); }
    } finally { ClosePrinter(h); }
  }
}
"@
[RawPrint]::Send($Printer, [System.IO.File]::ReadAllBytes($File))
`;

const SERIAL_PS = String.raw`
param([string]$Port, [int]$Baud, [string]$File)
$ErrorActionPreference = 'Stop'
$bytes = [System.IO.File]::ReadAllBytes($File)
$sp = New-Object System.IO.Ports.SerialPort $Port, $Baud, 'None', 8, 'One'
$sp.WriteTimeout = 5000
$sp.Open()
try { $sp.Write($bytes, 0, $bytes.Length); $sp.BaseStream.Flush() } finally { $sp.Close() }
`;

function runPowerShell(script: string, args: string[], bytes: Uint8Array): Promise<void> {
  return (async () => {
    const dir = await mkdtemp(join(tmpdir(), 'sgprint-'));
    try {
      const ps1 = join(dir, 'job.ps1');
      const bin = join(dir, 'job.bin');
      await writeFile(ps1, '﻿' + script, 'utf8'); // BOM so PowerShell 5 reads UTF-8
      await writeFile(bin, bytes);
      await new Promise<void>((resolve, reject) => {
        execFile(
          'powershell.exe',
          ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', ps1, ...args.map((a) => (a === '{FILE}' ? bin : a))],
          { timeout: 20_000, windowsHide: true },
          (err, _out, stderr) => (err ? reject(new Error(stderr?.toString().trim() || err.message)) : resolve()),
        );
      });
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  })();
}

function sendTcp(host: string, port: number, bytes: Uint8Array): Promise<void> {
  return new Promise((resolve, reject) => {
    const sock = net.connect({ host, port });
    sock.setTimeout(5000, () => sock.destroy(new Error(`Принтер ${host}:${port} не відповідає (таймаут).`)));
    sock.once('error', reject);
    sock.once('connect', () => sock.end(Buffer.from(bytes), () => resolve()));
  });
}

export async function sendBytes(s: PrintSettings, bytes: Uint8Array): Promise<void> {
  switch (s.transport) {
    case 'file':
      if (!s.filePath) throw new Error('Не вказано файл для віртуального принтера.');
      return appendFile(s.filePath, bytes);
    case 'tcp':
      if (!s.tcpHost) throw new Error('Не вказано адресу мережевого принтера.');
      return sendTcp(s.tcpHost, s.tcpPort || 9100, bytes);
    case 'serial':
      if (!/^COM\d+$/i.test(s.serialPort)) throw new Error('Вкажіть COM-порт, напр. COM3.');
      return runPowerShell(SERIAL_PS, [s.serialPort, String(s.serialBaud || 9600), '{FILE}'], bytes);
    case 'windows':
      if (!s.printerName) throw new Error('Оберіть встановлений принтер.');
      return runPowerShell(RAW_SPOOLER_PS, [s.printerName, '{FILE}'], bytes);
  }
}
