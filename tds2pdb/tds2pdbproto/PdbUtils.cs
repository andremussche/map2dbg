using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace tds2pdbproto
{
	class GlobalsHash
	{
		private const int HASH_MODULO = 0x1000;
		private BitArray m_occupiedHash = new BitArray(HASH_MODULO);
		private List<KeyValuePair<string, SymbolInfo>>[] m_table = new List<KeyValuePair<string, SymbolInfo>>[HASH_MODULO];
		private int m_numGlobals = 0;

		private void Add(string name, SymbolInfo symbol)
		{
			int hashVal = (int)PdbWriter.Hash(name, HASH_MODULO);
			if (!m_occupiedHash[hashVal])
			{
				m_occupiedHash[hashVal] = true;
				m_table[hashVal] = new List<KeyValuePair<string, SymbolInfo>>();
			}
			m_table[hashVal].Add(new KeyValuePair<string,SymbolInfo>(name, symbol));
			m_numGlobals++;
		}

		public void Add(SymbolInfo symbol)
		{
			if (symbol is SymData32)
				Add(((SymData32)symbol).Name, symbol);
			else if (symbol is SymGProcRef)
				Add(((SymGProcRef)symbol).Name, symbol);
			else
				throw new ApplicationException("Invalid symbol type in call to GlobalsHash.Add(): " + symbol.ToString());
		}

		public void Serialize(Stream stm)
		{
			BinaryWriter wrr = new BinaryWriter(stm);
			wrr.Write((int)-1);
			wrr.Write((uint)0xf12f091a);
			wrr.Write((int)(m_numGlobals * 8)); // offset items length
			int countBits = 0;
			foreach (bool bit in m_occupiedHash)
			{
				if (bit)
					countBits++;
			}
			wrr.Write((int)(0x204 + 4 * countBits)); // compressed hash length
			foreach (List<KeyValuePair<string, SymbolInfo>> syms in m_table)
			{
				if (syms == null)
					continue;
				foreach (KeyValuePair<string, SymbolInfo> sym in syms)
				{
					wrr.Write((int)sym.Value.Tag + 1);
					wrr.Write((int)1);
				}
			}
			uint dword = 0;
			int counter = 0;
			foreach (bool bit in m_occupiedHash)
			{
				if (counter % 32 == 0)
				{
					if (counter != 0)
						wrr.Write(dword);
					dword = 0;
				}
				if (bit)
					dword |= 1u << (counter % 32);
				counter++;
			}
			wrr.Write(dword);
			wrr.Write((int)0);
			int offset = 0;
			foreach (List<KeyValuePair<string,SymbolInfo>> syms in m_table)
			{
				if (syms == null)
					continue;
				wrr.Write(offset);
				foreach (KeyValuePair<string, SymbolInfo> sym in syms)
					offset += 12;
			}
		}
	}
}
