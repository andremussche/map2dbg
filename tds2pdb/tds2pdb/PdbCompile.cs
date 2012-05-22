using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace tds2pdbproto
{
    [StructLayout(LayoutKind.Sequential)]
    struct Header
    {
        public uint PageSize;
        public uint StartPage;
        public uint FilePages;
        public uint RootStreamSize;
        public uint Reserved;
        public uint RootPageIndex;
    }

    public class PdbCompiler
    {
        static uint Int2UInt(int val)
        {
            if (val < uint.MinValue)
                throw new ApplicationException("Numeric overflow");
            return (uint)val;
        }

        static uint Long2UInt(long val)
        {
            if (val < uint.MinValue || uint.MaxValue < val)
                throw new ApplicationException("Numeric overflow");
            return (uint)val;
        }

        static int Long2Int(long val)
        {
            if (val < int.MinValue || int.MaxValue < val)
                throw new ApplicationException("Numeric overflow");
            return (int)val;
        }

        static int UInt2Int(uint val)
        {
            if (int.MaxValue < val)
                throw new ApplicationException("Numeric overflow");
            return (int)val;
        }

        static uint NumPages(uint datasize, uint pagesize)
        {
            return (datasize + pagesize - 1) / pagesize;
        }

        static int PageRemains(uint datasize, uint pagesize)
        {
            return UInt2Int(NumPages(datasize, pagesize) * pagesize - datasize);
        }

        static byte[] GetBytes(object struc)
        {
            int len = Marshal.SizeOf(struc);
            byte[] arr = new byte[len];
            IntPtr ptr = Marshal.AllocCoTaskMem(len);
            try
            {
                Marshal.StructureToPtr(struc, ptr, true);
                Marshal.Copy(ptr, arr, 0, len);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
            return arr;
        }

        static void PumpStreams(Stream from, Stream to)
        {
            byte[] buffer = new byte[4096];
            while (true)
            {
                int read = from.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    return;
                to.Write(buffer, 0, read);
            }
        }

        public static void Compile(Stream[] streams, Stream output, uint pageSize)
        {
            // ðàñ÷èòûâàåì ðàçìåðû
            uint streamsNumPages = 0;
            uint numStreams = Int2UInt(streams.Length);
            uint[] lengths = new uint[numStreams];
            for (int i = 0; i < numStreams; i++)
            {
                lengths[i] = Long2UInt(streams[i].Length) - Long2UInt(streams[i].Position);
                streamsNumPages += NumPages(lengths[i], pageSize);
            }
            uint rootStmSize = 0;
            uint rootStmNumPages = 0;
            while (true)
            {
                uint wasRootStmSize = rootStmSize;
                rootStmNumPages = NumPages(rootStmSize, pageSize);
                rootStmSize = 4 + 4 * (numStreams + 1) + 4 * (streamsNumPages + rootStmNumPages);
                if (wasRootStmSize == rootStmSize)
                    break;
            }
            uint rootStmIndexSize = rootStmNumPages * 4;
            uint rootStmIndexNumPages = NumPages(rootStmIndexSize, pageSize);
            uint filePages = 0;
            uint bitmapNumPages = 0;
            uint bitmapSizeBits = 0;
            while (true)
            {
                uint wasBitmapSizeBits = bitmapSizeBits;
                bitmapNumPages = NumPages(bitmapSizeBits, pageSize * 8);
                filePages = 1 + rootStmNumPages + streamsNumPages +
                    rootStmIndexNumPages + bitmapNumPages;
                bitmapSizeBits = filePages;
                if (wasBitmapSizeBits == bitmapSizeBits)
                    break;
            }
            uint startPage = 1 + bitmapNumPages;

            byte[] pad = new byte[pageSize - 1];
            byte[] onespad = new byte[pageSize - 1];
            for (int i = 0; i < onespad.Length; i++)
                onespad[i] = 0xff;
            byte[] buffer;
            // ïèøåì çàãîëîâîê
            const string SIGNATURE = "Microsoft C/C++ MSF 7.00\r\n\x001ADS\0\0\0";
            byte[] signature = Encoding.ASCII.GetBytes(SIGNATURE);
            output.Write(signature, 0, signature.Length);
            Header hdr = new Header();
            hdr.PageSize = pageSize;
            hdr.StartPage = startPage;
            hdr.FilePages = filePages;
            hdr.RootStreamSize = rootStmSize;
            hdr.RootPageIndex = filePages - rootStmIndexNumPages;
            buffer = GetBytes(hdr);
            output.Write(buffer, 0, buffer.Length);
            output.Write(pad, 0, PageRemains(Int2UInt(signature.Length +
                Marshal.SizeOf(typeof(Header))), pageSize));

            // ïèøåì áèòìàï
            byte[] bitmap = new byte[NumPages(bitmapSizeBits, 8)];
            byte lastByte = 0xff;
            byte bit = 1;
            uint lastByteBits = bitmapSizeBits - bitmapSizeBits / 8 * 8;
            for (uint i = 0; i < lastByteBits; i++)
            {
                lastByte &= (byte)~bit;
                bit <<= 1;
            }
            bitmap[bitmap.Length - 1] = lastByte;
            output.Write(bitmap, 0, bitmap.Length);
            output.Write(onespad, 0, PageRemains(NumPages(bitmapSizeBits, 8), pageSize));

            // çàïèñûâàåì êîðíåâîé ñòðèì (îãëàâëåíèå)
            buffer = BitConverter.GetBytes(numStreams + 1);
            output.Write(buffer, 0, buffer.Length);
            // ïèøåì ðàçìåð êîðíåâîãî (0-ãî) ñòðèìà
            buffer = BitConverter.GetBytes(rootStmSize);
            output.Write(buffer, 0, buffer.Length);
            // ïèøåì ðàçìåðû îñòàëüíûõ ñòðèìîâ
            foreach (uint len in lengths)
            {
                buffer = BitConverter.GetBytes(len);
                output.Write(buffer, 0, buffer.Length);
            }
            // ïèøåì èíäåêñû ñòðàíèö êîðíåâîãî ñòðèìà
            uint rootStmStartPage = startPage;
            uint pageIndex = rootStmStartPage;
            for (uint i = 0; i < rootStmNumPages; i++)
            {
                buffer = BitConverter.GetBytes(pageIndex);
                output.Write(buffer, 0, buffer.Length);
                pageIndex++;
            }
            // ïèøåì èíäåêñû ñòðàíèö îñòàëüíûõ ñòðèìîâ
            for (uint s = 0; s < numStreams; s++)
            {
                for (uint i = 0; i < NumPages(lengths[s], pageSize); i++)
                {
                    buffer = BitConverter.GetBytes(pageIndex);
                    output.Write(buffer, 0, buffer.Length);
                    pageIndex++;
                }
            }
            output.Write(pad, 0, PageRemains(rootStmSize, pageSize));

            // çàïèñûâàåì ñòðèìû
            for (uint i = 0; i < numStreams; i++)
            {
                if (lengths[i] == 0)
                    continue;
                PumpStreams(streams[i], output);
                output.Write(pad, 0, PageRemains(lengths[i], pageSize));
            }

            // çàïèñûâàåì èíäåêñ îãëàâëåíèÿ
            Debug.Assert(pageIndex == hdr.RootPageIndex);
            pageIndex = rootStmStartPage;
            for (uint i = 0; i < rootStmNumPages; i++)
            {
                buffer = BitConverter.GetBytes(pageIndex);
                output.Write(buffer, 0, buffer.Length);
                pageIndex++;
            }
            output.Write(pad, 0, PageRemains(rootStmIndexSize, pageSize));
        }
    }

}
