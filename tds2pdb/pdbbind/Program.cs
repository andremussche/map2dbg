using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace pdbbind
{
	public class PdbBind
	{
		struct CoffHeader
		{
			public short Machine;
			public short NumberOfSections;
			public int TimeDateStamp;
			public int PointerToSymbolTable;
			public int NumberOfSymbols;
			public short SizeOfOptionalHeader;
			public short Flags;
		}

		struct SectionHeader
		{
			public string Name;
			public int VirtualSize;
			public int Rva;
			public int FileSize;
			public int FileOffset;
			public int RelocOffset;
			public int LineOffset;
			public short RelocNum;
			public short LineNum;
			public int Flags;
		}

		static int RoundUp(int value, int boundary)
		{
			return (value + boundary - 1) / boundary * boundary;
		}

		public static int Bind(string exefname, string pdbfname, int timestamp, Guid guid, int age)
		{
			pdbfname = Path.GetFileName(pdbfname);

			FileStream stm = File.Open(exefname, FileMode.Open, FileAccess.ReadWrite);
			BinaryReader rdr = new BinaryReader(stm);
			BinaryWriter wrr = new BinaryWriter(stm);

			byte[] utf8pdbname = Encoding.UTF8.GetBytes(pdbfname);
			int debugDirectorySize = 0x1C;
			int debugInfoSize = 0x18 + utf8pdbname.Length + 1;
			int debugSize = debugDirectorySize + debugInfoSize;

			short magic = rdr.ReadInt16();
			if (magic != 0x5a4d)
			{
				Console.WriteLine("Error: invalid magic in DOS header");
				return 1;
			}
			stm.Position = 0x3c;
			int peoffset = rdr.ReadInt32();
			stm.Position = peoffset;
			int pemagic = rdr.ReadInt32();
			if (pemagic != 0x4550)
			{
				Console.WriteLine("Error: invalid pe magic");
				return 1;
			}
			CoffHeader coffHdr = new CoffHeader();
			coffHdr.Machine = rdr.ReadInt16();
			coffHdr.NumberOfSections = rdr.ReadInt16();
			coffHdr.TimeDateStamp = rdr.ReadInt32();
			coffHdr.PointerToSymbolTable = rdr.ReadInt32();
			coffHdr.NumberOfSymbols = rdr.ReadInt32();
			coffHdr.SizeOfOptionalHeader = rdr.ReadInt16();
			coffHdr.Flags = rdr.ReadInt16();
			int optionalHeaderOffset = (int)stm.Position;
			int sectionsOffset = optionalHeaderOffset + coffHdr.SizeOfOptionalHeader;
			short optmagic = rdr.ReadInt16();
			if (optmagic != 0x10b)
			{
				Console.WriteLine("Error: invalid or unsupported optional header magic");
				return 1;
			}
			stm.Position = optionalHeaderOffset + 32;
			int sectionAlignment = rdr.ReadInt32();
			int fileAlignment = rdr.ReadInt32();
			int numberOfDirEntriesOffset = optionalHeaderOffset + 92;
			stm.Position = numberOfDirEntriesOffset;
			int numberOfDirEntries = rdr.ReadInt32();
			const int DEBUG_TABLE = 6;
			if (numberOfDirEntries < DEBUG_TABLE + 1)
			{
				stm.Position = numberOfDirEntriesOffset;
				wrr.Write(DEBUG_TABLE + 1);
			}
			int debugTableEntryOffset = optionalHeaderOffset + 144;
			int debugTableEntryEnd = optionalHeaderOffset + 152;
			if (debugTableEntryEnd > sectionsOffset)
			{
				Console.WriteLine("Error: not enough space for Debug directory entry, optional header must be resized, not implemented");
				return 1;
			}
			stm.Position = sectionsOffset;
			SectionHeader[] sectionHeaders = new SectionHeader[coffHdr.NumberOfSections];
			for (int i = 0; i < coffHdr.NumberOfSections; i++)
			{
				byte[] encname = rdr.ReadBytes(8);
				int l = 0;
				for (l = 0; l < 8; l++)
					if (encname[l] == 0)
						break;
				sectionHeaders[i].Name = Encoding.UTF8.GetString(encname, 0, l);
				sectionHeaders[i].VirtualSize = rdr.ReadInt32();
				sectionHeaders[i].Rva = rdr.ReadInt32();
				sectionHeaders[i].FileSize = rdr.ReadInt32();
				sectionHeaders[i].FileOffset = rdr.ReadInt32();
				sectionHeaders[i].RelocOffset = rdr.ReadInt32();
				sectionHeaders[i].LineOffset = rdr.ReadInt32();
				sectionHeaders[i].RelocNum = rdr.ReadInt16();
				sectionHeaders[i].LineNum = rdr.ReadInt16();
				sectionHeaders[i].Flags = rdr.ReadInt32();
			}
			int rdataIndex = -1;
			for (int i = 0; i < coffHdr.NumberOfSections; i++)
			{
				if (sectionHeaders[i].Name == ".rdata")
				{
					rdataIndex = i;
					break;
				}
			}
			SectionHeader rdataSect = sectionHeaders[rdataIndex];
			if (rdataIndex == -1)
			{
				Console.WriteLine("Error: .rdata section not found");
				return 1;
			}
			int debugSizeFileAligned = RoundUp(debugSize, fileAlignment);
			int newRdataSizeFileAligned = rdataSect.FileSize + debugSizeFileAligned;
			int newRdataSizeSectionAligned = RoundUp(newRdataSizeFileAligned, sectionAlignment);
			if (rdataSect.VirtualSize < newRdataSizeSectionAligned)
			{
				Console.WriteLine("Error: crossing section alignment boundary, relocation requred, not implemented");
				return 1;
			}
			// injecting debug data
			// moving remains data
			int debugDataOffset = rdataSect.FileOffset + rdataSect.FileSize;
			int debugDataEndAligned = debugDataOffset + debugSizeFileAligned;
			stm.Position = debugDataOffset;
			int remainsSize = (int)(stm.Length - stm.Position);
			byte[] remainsBuffer = new byte[remainsSize];
			stm.Read(remainsBuffer, 0, remainsSize);
			stm.Position = debugDataEndAligned;
			stm.Write(remainsBuffer, 0, remainsSize);
			// writing debug data directory
			stm.Position = debugDataOffset;
			wrr.Write((int)0); // charateristics, reserved
			wrr.Write((int)timestamp);
			wrr.Write((int)0x0); // version of debugging information format
			wrr.Write((int)2); // CodeView type
			wrr.Write(debugInfoSize);
			int debugInfoRva = rdataSect.Rva + rdataSect.FileSize + debugDirectorySize;
			wrr.Write(debugInfoRva);
			int debugInfoFileOffset = rdataSect.FileOffset + rdataSect.FileSize + debugDirectorySize;
			wrr.Write(debugInfoFileOffset);
			// writing debug info
			wrr.Write(Encoding.UTF8.GetBytes("RSDS"));
			wrr.Write(guid.ToByteArray());
			wrr.Write(age);
			wrr.Write(utf8pdbname);
			wrr.Write((byte)0);
			int paddingSize = fileAlignment - (debugSize - debugSize / fileAlignment * fileAlignment);
			byte[] pad = new byte[paddingSize];
			wrr.Write(pad);
			// updating debug directory address in table
			int debugDirectoryRva = rdataSect.Rva + rdataSect.FileSize;
			stm.Position = debugTableEntryOffset;
			wrr.Write(debugDirectoryRva);
			wrr.Write(debugDirectorySize);
			// update rdata file size
			int sectionHeaderSize = 40;
			int sectionHeaderFileSizeFieldOffset = 16;
			int sectionHeaderFileOffsetFieldOffset = 20;
			stm.Position = sectionsOffset + sectionHeaderSize * rdataIndex + sectionHeaderFileSizeFieldOffset;
			wrr.Write(newRdataSizeFileAligned);
			// updating sections offsets
			int delta = debugSizeFileAligned;
			for (int i = rdataIndex + 1; i < coffHdr.NumberOfSections; i++)
			{
				sectionHeaders[i].FileOffset += delta;
				stm.Position = sectionsOffset + sectionHeaderSize * i + sectionHeaderFileOffsetFieldOffset;
				wrr.Write(sectionHeaders[i].FileOffset);
			}
			stm.Close();
			return 0;
		}
	}

	class Program
	{
		static int Main(string[] args)
		{
			if (args.Length < 4)
			{
				Console.WriteLine("pdbbind exefile timestamp guid age");
				return 0;
			}
			string exefileStr = args[0];
			string timestampStr = args[1];
			string guidStr = args[2];
			string ageStr = args[3];
			int timestamp = int.Parse(timestampStr);
			Guid guid = new Guid(guidStr);
			int age = int.Parse(ageStr);
			string pdbname = Path.GetFileNameWithoutExtension(exefileStr) + ".pdb";
			return PdbBind.Bind(exefileStr, pdbname, timestamp, guid, age);
		}
	}
}
