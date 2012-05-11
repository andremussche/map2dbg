using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace tds2pdbproto
{
	class PdbWriterVisitor : ITypeInfoVisitor, ISymbolVisitor, ITypMemberVisitor
	{
		public BinaryWriter wrr;

		public void Visit(PrimitiveTypeInfo primType)
		{
			Debug.Fail("unexpected type (primitivetype) in PdbWriterVisitor");
		}

		public void Visit(TypModifier modifierType)
		{
			wrr.Write((short)PdbWriter.CV_LF_MODIFIER);
			wrr.Write((int)PdbWriter.TypeInfoToId(modifierType.Type.Ref));
			wrr.Write((short)modifierType.Attribute);
		}

		public void Visit(TypPointer pointerType)
		{
			wrr.Write((short)PdbWriter.CV_LF_POINTER);
			wrr.Write(PdbWriter.TypeInfoToId(pointerType.PointedType.Ref));
			wrr.Write(pointerType.Attribute.GetCvBytes());
			switch (pointerType.Attribute.Mode)
			{
				case PointerMode.PtrToMethod:
					wrr.Write((short)0);
					wrr.Write(PdbWriter.TypeInfoToId(pointerType.ClassType.Ref));
					wrr.Write((short)0);
					break;
			}
		}

		public void Visit(TypArray arrayType)
		{
			wrr.Write((short)PdbWriter.CV_LF_ARRAY);
			wrr.Write((int)PdbWriter.TypeInfoToId(arrayType.ElementType.Ref));
			wrr.Write((int)PdbWriter.TypeInfoToId(arrayType.IndexType.Ref));
			wrr.Write(arrayType.Length.GetBytes());
			PdbWriter.WriteUTF8Z(wrr, arrayType.Name);
		}

		class MemberCounter : ITypMemberVisitor
		{
			public int Count = 0;

			public void Visit(MemRealBaseClass realbase) { Count++; }
			public void Visit(MemVirtualBaseClass virtbase) { Count++; }
			public void Visit(MemInstanceData instdata)
			{
				if (instdata.Type.Ref is TypPasProperty)
					return;
				Count++;
			}
			public void Visit(MemStaticData statdata) { Count++; }
			public void Visit(MemMethod method) { Count++; }
			public void Visit(MemMethods method) { Count += method.List.Ref.Methods.Count; }
			public void Visit(MemNestedType nesttype) { Count++; }
			public void Visit(MemVtabPointer vtabptr) { Count++; }

			public void Visit(MemEnumerate memenum)
			{
				throw new Exception("The method or operation is not implemented.");
			}

			public void Visit(MemIndex index)
			{
				TypMembersList memlist = index.Continuation as TypMembersList;
				if (memlist != null)
				{
					foreach (TypMember member in memlist.SubFields)
						member.Visit(this);
				}
				else
				{
					throw new Exception("The method or operation is not implemented.");
				}
			}
		}

		public void Visit(TypStruct structType)
		{
			wrr.Write((short)(structType.IsClass ? PdbWriter.CV_LF_CLASS : PdbWriter.CV_LF_STRUCTURE));
			MemberCounter membCntr = new MemberCounter();
			if (structType.Members != null)
			{
				foreach (TypMember member in structType.Members.Ref.SubFields)
					member.Visit(membCntr);
			}
			wrr.Write((short)membCntr.Count);
			PdbWriter.WriteStructFlags(wrr, structType.Flags);
			wrr.Write((int)PdbWriter.TypeInfoToId(structType.Members.Ref));
			wrr.Write((int)PdbWriter.TypeInfoToId(structType.DerivationList.Ref));
			wrr.Write((int)PdbWriter.TypeInfoToId(structType.VTable.Ref));
			wrr.Write(structType.Size.GetBytes());
			PdbWriter.WriteUTF8Z(wrr, structType.Name);
		}

		public void Visit(TypUnion unionType)
		{
			wrr.Write((short)PdbWriter.CV_LF_UNION);
			MemberCounter membCntr = new MemberCounter();
			if (unionType.Members != null)
			{
				foreach (TypMember member in unionType.Members.Ref.SubFields)
					member.Visit(membCntr);
			}
			wrr.Write((short)membCntr.Count);
			PdbWriter.WriteStructFlags(wrr, unionType.Flags);
			wrr.Write((int)PdbWriter.TypeInfoToId(unionType.Members.Ref));
			wrr.Write(unionType.Size.GetBytes());
			PdbWriter.WriteUTF8Z(wrr, unionType.Name);
		}

		public void Visit(TypEnum enumType)
		{
			wrr.Write((short)PdbWriter.CV_LF_ENUM);
			wrr.Write((short)enumType.Count);
			wrr.Write((short)0); // property
			wrr.Write((int)PdbWriter.TypeInfoToId(enumType.Type.Ref));
			wrr.Write((int)PdbWriter.TypeInfoToId(enumType.Values.Ref));
			PdbWriter.WriteUTF8Z(wrr, enumType.Name);
		}

		public void Visit(TypProcedure procType)
		{
			wrr.Write((short)PdbWriter.CV_LF_PROCEDURE);
			int retTypeId = PdbWriter.TypeInfoToId(procType.RetType.Ref);
			wrr.Write(retTypeId);
			PdbWriter.WriteCallConv(wrr, procType.CallConv);
			wrr.Write((byte)0); // reserved
			wrr.Write((short)procType.ArgsRef.Ref.Args.Length);
			int argListTypeId = PdbWriter.TypeInfoToId(procType.ArgsRef.Ref);
			wrr.Write(argListTypeId);
		}

		public void Visit(TypMFunction mfuncType)
		{
			wrr.Write((short)PdbWriter.CV_LF_MFUNCTION);
			int retTypeId = PdbWriter.TypeInfoToId(mfuncType.RetType.Ref);
			wrr.Write(retTypeId);
			wrr.Write((int)PdbWriter.TypeInfoToId(mfuncType.ClassType.Ref));
			wrr.Write((int)PdbWriter.TypeInfoToId(mfuncType.ThisType.Ref));
			PdbWriter.WriteCallConv(wrr, mfuncType.CallConv);
			wrr.Write((byte)0); // reserved
			wrr.Write((short)mfuncType.NumArgs);
			int argListTypeId = PdbWriter.TypeInfoToId(mfuncType.Args.Ref);
			wrr.Write(argListTypeId);
			wrr.Write((int)mfuncType.ThisAdjust);
		}

		public void Visit(TypVtabShape vtabShape)
		{
			wrr.Write((short)PdbWriter.CV_LF_VTSHAPE);
			int numDescrs = vtabShape.Descriptors.Count;
			wrr.Write((short)numDescrs);
			byte descrByte = 0;
			for (int i = 0; i < numDescrs; i++)
			{
				int descr = ((int)vtabShape.Descriptors[i]) & 0xF;
				if (i % 2 == 0)
				{
					descrByte = (byte)descr;
				}
				else
				{
					descrByte = (byte)(descrByte | (descr << 4));
					wrr.Write(descrByte);
				}
			}
			if (numDescrs % 2 == 1)
				wrr.Write(descrByte);
		}

		public void Visit(TypLabel lable)
		{
            //am			throw new ApplicationException("Label not implemented");
		}

		public void Visit(TypPasSet set)
		{
            //am			throw new ApplicationException("PasSet not implemented");
		}

		public void Visit(TypPasSubRange subrange)
		{
            //am			throw new ApplicationException("PasSubRange not implemented");
		}

		public void Visit(TypPasPArray parray)
		{
            //am			throw new ApplicationException("PasPArray not implemented");
		}

		public void Visit(TypPasPString pstring)
		{
            //am			throw new ApplicationException("PasPString not implemented");
		}

		public void Visit(TypPasClosure closure)
		{
            //am  throw new ApplicationException("PasClosure not implemented");
		}

		public void Visit(TypPasProperty prop)
		{
            //am			throw new ApplicationException("PasProperty not implemented");
		}

		public void Visit(TypPasLString lstring)
		{
            //am			throw new ApplicationException("PasLString not implemented");
		}

		public void Visit(TypPasVariant variant)
		{
            //am			throw new ApplicationException("PasVariant not implemented");
		}

		public void Visit(TypPasClassRef classref)
		{
            //am			throw new ApplicationException("PasClassRef not implemented");
		}

		public void Visit(TypPasUnknown39 unknown39)
		{
            //am			throw new ApplicationException("PasUnknown39 not implemented");
		}

		public void Visit(TypArgList argsType)
		{
			wrr.Write((short)PdbWriter.CV_LF_ARGLIST);
			wrr.Write((short)argsType.Args.Length);
			wrr.Write((short)0); // reserved
			for (int i = 0; i < argsType.Args.Length; i++)
				wrr.Write(PdbWriter.TypeInfoToId(argsType.Args[i].Ref));
		}

		public void Visit(TypMembersList fieldsType)
		{
			wrr.Write((short)PdbWriter.CV_LF_LIST);
			foreach (TypMember subField in fieldsType.SubFields)
			{
				MemInstanceData dataMemb = subField as MemInstanceData;
				if (dataMemb != null)
				{
					if (dataMemb.Type.Ref is TypPasProperty)
						continue;
				}
				subField.Visit(this);
				PdbWriter.AlignTypes4(wrr);
			}
		}

		public void Visit(TypBitField bitField)
		{
			wrr.Write((short)PdbWriter.CV_LF_BITFIELD);
			wrr.Write((int)PdbWriter.TypeInfoToId(bitField.Type));
			wrr.Write((byte)bitField.Length);
			wrr.Write((byte)bitField.Pos);
		}

		public void Visit(TypMList mlistType)
		{
			wrr.Write((short)PdbWriter.CV_LF_MLIST);
			foreach (MListItem item in mlistType.Methods)
			{
				PdbWriter.WriteMemberAttributes(wrr, item.Attributes);
				wrr.Write((short)0); // reserved
				wrr.Write(PdbWriter.TypeInfoToId(item.Func.Ref));
				if (item.Attributes.MProp == MPropType.IntroducingVirtual ||
					item.Attributes.MProp == MPropType.PureIntroducingVirtual)
				{
					wrr.Write((int)item.VTabOffset);
				}
			}
		}

		public void Visit(MemRealBaseClass realBaseClass)
		{
			wrr.Write((short)PdbWriter.CV_LF_BCLASS);
			PdbWriter.WriteMemberAttributes(wrr, realBaseClass.Attribute);
			wrr.Write((int)PdbWriter.TypeInfoToId(realBaseClass.Class.Ref));
			wrr.Write(realBaseClass.Offset.GetBytes());
		}

		public void Visit(MemVirtualBaseClass virtualBaseClass)
		{
			wrr.Write((short)(virtualBaseClass.IsDirect ? PdbWriter.CV_LF_VBCLASS : PdbWriter.CV_LF_IVBCLASS));
			PdbWriter.WriteMemberAttributes(wrr, virtualBaseClass.Attribute);
			wrr.Write((int)PdbWriter.TypeInfoToId(virtualBaseClass.Class));
			wrr.Write((int)PdbWriter.TypeInfoToId(virtualBaseClass.PtrType));
			wrr.Write(virtualBaseClass.Offset.GetBytes());
			wrr.Write(virtualBaseClass.VbaseDispIndex.GetBytes());
		}

		public void Visit(MemEnumerate enumerateType)
		{
			wrr.Write((short)PdbWriter.CV_LF_ENUMERATE);
			wrr.Write((short)enumerateType.Attribute);
			wrr.Write(enumerateType.Value.GetBytes());
			PdbWriter.WriteUTF8Z(wrr, enumerateType.Name);
		}

		public void Visit(MemIndex index)
		{
			wrr.Write((short)PdbWriter.CV_LF_INDEX);
			wrr.Write((int)PdbWriter.TypeInfoToId(index.Continuation));
		}

		public void Visit(MemInstanceData dataMemberType)
		{
			wrr.Write((short)PdbWriter.CV_LF_MEMBER);
			PdbWriter.WriteMemberAttributes(wrr, dataMemberType.Attributes);
			wrr.Write((int)PdbWriter.TypeInfoToId(dataMemberType.Type.Ref));
			wrr.Write(dataMemberType.Offset.GetBytes());
			PdbWriter.WriteUTF8Z(wrr, dataMemberType.Name);
		}

		public void Visit(MemStaticData staticDataMember)
		{
			wrr.Write((short)PdbWriter.CV_LF_STMEMBER);
			PdbWriter.WriteMemberAttributes(wrr, staticDataMember.Attributes);
			wrr.Write((int)PdbWriter.TypeInfoToId(staticDataMember.Type));
			PdbWriter.WriteUTF8Z(wrr, staticDataMember.Name);
		}

		public void Visit(MemMethod method)
		{
			wrr.Write((short)PdbWriter.CV_LF_ONEMETHOD);
			PdbWriter.WriteMemberAttributes(wrr, method.Attributes);
			wrr.Write(PdbWriter.TypeInfoToId(method.MethodType));
			if (method.Attributes.MProp == MPropType.IntroducingVirtual ||
				method.Attributes.MProp == MPropType.PureIntroducingVirtual)
			{
				wrr.Write((int)method.VTabOffset);
			}
			PdbWriter.WriteUTF8Z(wrr, method.Name);
		}

		public void Visit(MemMethods methodMemberType)
		{
			int numMethods = methodMemberType.List.Ref.Methods.Count;
			if (numMethods == 1)
			{
				wrr.Write((short)PdbWriter.CV_LF_ONEMETHOD);
				MListItem method = methodMemberType.List.Ref.Methods[0];
				PdbWriter.WriteMemberAttributes(wrr, method.Attributes);
				wrr.Write(PdbWriter.TypeInfoToId(method.Func.Ref));
				if (method.Attributes.MProp == MPropType.IntroducingVirtual ||
					method.Attributes.MProp == MPropType.PureIntroducingVirtual)
				{
					wrr.Write((int)method.VTabOffset);
				}
				PdbWriter.WriteUTF8Z(wrr, methodMemberType.Name);
			}
			else
			{
				wrr.Write((short)PdbWriter.CV_LF_METHOD);
				wrr.Write((short)numMethods);
				wrr.Write((int)PdbWriter.TypeInfoToId(methodMemberType.List.Ref));
				PdbWriter.WriteUTF8Z(wrr, methodMemberType.Name);
			}
		}

		public void Visit(MemNestedType nestedType)
		{
			wrr.Write((short)PdbWriter.CV_LF_NESTEDTYPE);
			wrr.Write((short)0); // reserved
			wrr.Write((int)PdbWriter.TypeInfoToId(nestedType.NestedType.Ref));
			PdbWriter.WriteUTF8Z(wrr, nestedType.Name);
		}

		public void Visit(MemVtabPointer vtabPointer)
		{
			int offset = ((IConvertible)vtabPointer.Offset.GetValue()).ToInt32(null);
			if (offset != 0)
			{
				wrr.Write((short)PdbWriter.CV_LF_VFUNCOFF);
				wrr.Write((short)0); // reserved
				wrr.Write((int)PdbWriter.TypeInfoToId(vtabPointer.VtabShapePtr.Ref));
				wrr.Write(offset);
			}
			else
			{
				wrr.Write((short)PdbWriter.CV_LF_VFUNCTAB);
				wrr.Write((short)0); // reserved
				wrr.Write((int)PdbWriter.TypeInfoToId(vtabPointer.VtabShapePtr.Ref));
			}
		}

		public void Visit(SymCompile comp)
		{
			short type = PdbWriter.CV_S_COMPILE;
			wrr.Write(type);
			wrr.Write((byte)comp.Language);
			wrr.Write((byte)0); // flags: 1 = compiled for EnC
			wrr.Write((ushort)0); // seems not used
			wrr.Write((short)comp.Machine);
			wrr.Write((short)1); // fe major
			wrr.Write((short)0); // fe minor
			wrr.Write((short)0); // fe build no
			wrr.Write((short)1); // be major
			wrr.Write((short)0); // be minor
			wrr.Write((short)0); // be build no
			PdbWriter.WriteUTF8Z(wrr, comp.Compiler);
		}

		public void Visit(SymRegister reg)
		{
			throw new ApplicationException("The method or operation is not implemented.");
		}

		public void Visit(SymConstant symConst)
		{
			wrr.Write((short)PdbWriter.CV_S_CONSTANT);
			wrr.Write((int)PdbWriter.TypeInfoToId(symConst.DataType));
			wrr.Write(symConst.Value.GetBytes());
			PdbWriter.WriteUTF8Z(wrr, symConst.Name);
		}

		public void Visit(SymSearch search)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymEnd end)
		{
			short type = PdbWriter.CV_S_END;
			wrr.Write(type);
		}

		public void Visit(SymGProcRef procRef)
		{
			wrr.Write(PdbWriter.CV_S_GPROCREF);
			wrr.Write((int)0);
			PdbWriter.FindProcResult fr = PdbWriter.FindReferencedProc(procRef.Segment, procRef.Offset);
			if (fr == null)
				throw new ApplicationException("Procedure for proc reference not found");
			wrr.Write((int)fr.Proc.Tag);
			wrr.Write((short)(fr.ModuleIndex + 1));
			PdbWriter.WriteUTF8Z(wrr, procRef.Name);
		}

		public void Visit(SymGDataRef dataRef)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymUdt udt)
		{
			wrr.Write((short)PdbWriter.CV_S_UDT);
			wrr.Write((int)PdbWriter.TypeInfoToId(udt.Type));
			PdbWriter.WriteUTF8Z(wrr, udt.Name);
		}

		public void Visit(SymBpRelative32Info bprel)
		{
			wrr.Write(PdbWriter.CV_S_BPREL32);
			wrr.Write((int)bprel.Offset);
			int typeId = PdbWriter.TypeInfoToId(bprel.DataType);
			wrr.Write(typeId);
			PdbWriter.WriteUTF8Z(wrr, bprel.Name);
		}

		public void Visit(SymData32 symData)
		{
			wrr.Write(symData.IsGlobal ? PdbWriter.CV_S_GDATA32 : PdbWriter.CV_S_LDATA32);
			int typeId = PdbWriter.TypeInfoToId(symData.DataType);
			wrr.Write(typeId);
			wrr.Write((int)symData.Offset);
			wrr.Write((short)symData.Segment);
			PdbWriter.WriteUTF8Z(wrr, symData.Name);
		}

		public void Visit(SymProc32Info procInfo)
		{
			short type = procInfo.IsGlobal ? PdbWriter.CV_S_GPROC32 : PdbWriter.CV_S_LPROC32;
			wrr.Write(type);
			wrr.Write((int)0); // parent
			wrr.Write((int)0); // end
			wrr.Write((int)0); // next
			wrr.Write((int)procInfo.Size);
			wrr.Write((int)procInfo.DebugStart);
			wrr.Write((int)procInfo.DebugEnd);
			int typeId = PdbWriter.TypeInfoToId(procInfo.ProcType);
			wrr.Write(typeId);
			wrr.Write((int)procInfo.Offset);
			wrr.Write((short)procInfo.Segment);
			wrr.Write((byte)0/*procInfo.Flags*/);
			PdbWriter.WriteUTF8Z(wrr, procInfo.Name);
		}

		public void Visit(SymThunk32 thunk32)
		{
			wrr.Write((short)PdbWriter.CV_S_THUNK32);
			wrr.Write((int)0); // parent
			wrr.Write((int)0); // end
			wrr.Write((int)0); // next
			wrr.Write((int)thunk32.Offset);
			wrr.Write((short)thunk32.Segment);
			wrr.Write((short)thunk32.Length);
			wrr.Write((byte)thunk32.Kind);
			PdbWriter.WriteUTF8Z(wrr, thunk32.Name);
			switch (thunk32.Kind)
			{
				case ThunkType.NoType:
					break;
				case ThunkType.Adjustor:
					PdbWriter.Align4(wrr);
					wrr.Write((int)thunk32.Adjust);
					PdbWriter.WriteUTF8Z(wrr, thunk32.FunctionName);
					break;
				default:
					Debug.Fail("Thunk kind not implemented: " + thunk32.Kind.ToString());
					break;
			}
		}

		public void Visit(SymBlock32 block32)
		{
			wrr.Write((short)PdbWriter.CV_S_BLOCK32);
			wrr.Write((int)0); // parent
			wrr.Write((int)0); // end
			wrr.Write((int)block32.Length);
			wrr.Write((int)block32.Offset);
			wrr.Write((short)block32.Segment);
			PdbWriter.WriteUTF8Z(wrr, block32.Name);
		}

		public void Visit(SymWith32 with)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymLabel32 label)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymEntry32 entry)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymOptVar32 optvar)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymProcRet32 symProcRet)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymSaveRegs32 saveregs)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymNamespace symNamespace)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymUses uses)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymUsing symUsing)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymPConstant pconstant)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(SymSLink32 slink)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}
	}


	class TypeListsMakerTyp : ITypeInfoVisitor, ITypMemberVisitor
	{
		private TypeListsMaker m_maker;

		public TypeListsMakerTyp(TypeListsMaker maker)
		{
			m_maker = maker;
		}

		private TypeInfo Add(TypeInfo type)
		{
			return m_maker.Add(type);
		}

		private void Add(BaseTypeRef typeref)
		{
			typeref.Reference = Add(typeref.Reference);
		}

		public void Visit(PrimitiveTypeInfo primType) { }

		public void Visit(TypModifier modifierType)
		{
			Add(modifierType.Type);
		}

		public void Visit(TypPointer pointerType)
		{
			Add(pointerType.PointedType);
			switch (pointerType.Attribute.Mode)
			{
				case PointerMode.Pointer:
				case PointerMode.Reference:
					break;
				case PointerMode.PtrToDataMember:
					Add(pointerType.ClassType);
					break;
				case PointerMode.PtrToMethod:
					Add(pointerType.ClassType);
					break;
			}
		}

		public void Visit(TypArray arrayType)
		{
			Add(arrayType.ElementType);
			Add(arrayType.IndexType);
		}

		public void Visit(TypStruct structType)
		{
			const string autoPtrPat = "std::auto_ptr<";
			if (structType.Name.Length > autoPtrPat.Length &&
				structType.Name.Substring(0, autoPtrPat.Length) == autoPtrPat)
			{
				foreach (TypMember memb in structType.Members.Ref.SubFields)
				{
					if (memb is MemInstanceData && ((MemInstanceData)memb).Name == "the_p")
					{
						((MemInstanceData)memb).Name = "_Myptr";
						break;
					}
				}
			}
			const string vectPat = "std::vector<";
			if (structType.Name.Length > vectPat.Length &&
				structType.Name.Substring(0, vectPat.Length) == vectPat)
			{
				foreach (TypMember memb in structType.Members.Ref.SubFields)
				{
					if (memb is MemInstanceData && ((MemInstanceData)memb).Name == "__start")
						((MemInstanceData)memb).Name = "_Myfirst";
					if (memb is MemInstanceData && ((MemInstanceData)memb).Name == "__finish")
						((MemInstanceData)memb).Name = "_Mylast";
					if (memb is MemInstanceData && ((MemInstanceData)memb).Name == "__end_of_storage")
						((MemInstanceData)memb).Name = "_Myend";
				}
			}
			Add(structType.ContainingType);
			Add(structType.Members);
			Add(structType.DerivationList);
			Add(structType.VTable);
		}

		public void Visit(TypUnion unionType)
		{
			Add(unionType.ContainingType);
			Add(unionType.Members);
		}

		public void Visit(TypEnum enumType)
		{
			Add(enumType.Type);
			Add(enumType.Values);
		}

		public void Visit(TypProcedure procType)
		{
			Add(procType.RetType);
			Add(procType.ArgsRef);
		}

		public void Visit(TypMFunction mfuncType)
		{
			Add(mfuncType.RetType);
			Add(mfuncType.ThisType);
			Add(mfuncType.ClassType);
			Add(mfuncType.Args);
		}

		public void Visit(TypVtabShape vtabShape)
		{
		}

		public void Visit(TypLabel label)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(TypPasSet set)
		{
			Add(set.BaseType);
		}

		public void Visit(TypPasSubRange subrange)
		{
			Add(subrange.BaseType);
		}

		public void Visit(TypPasPArray parray)
		{
//am			throw new Exception("The method or operation is not implemented.");
			/*Add(parray.ElementType);
			Add(parray.IndexType);*/
		}

		public void Visit(TypPasPString pstring)
		{
//am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(TypPasClosure closure)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(TypPasProperty prop)
		{
			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(TypPasLString lstring)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(TypPasVariant variant)
		{
            //am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(TypPasClassRef classref)
		{
//am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(TypPasUnknown39 unknown39)
		{
//am			throw new Exception("The method or operation is not implemented.");
		}

		public void Visit(TypArgList argsType)
		{
			foreach (TypeRef<TypeInfo> targ in argsType.Args)
				Add(targ);
		}

		public void Visit(TypMembersList fieldsType)
		{
			int i = 0;
			while (i < fieldsType.SubFields.Count)
			{
				TypMember memb = fieldsType.SubFields[i];
				if (memb is MemMethods && ((MemMethods)memb).List.Ref.Methods.Count == 1)
				{
					MemMethods methods = (MemMethods)memb;
					MemMethod method = new MemMethod();
					method.Name = methods.Name;
					method.Attributes = methods.List.Ref.Methods[0].Attributes;
					method.MethodType = methods.List.Ref.Methods[0].Func.Ref;
					method.VTabOffset = methods.List.Ref.Methods[0].VTabOffset;
					fieldsType.SubFields[i] = method;
					memb = method;
				}
				else if (memb is MemInstanceData && ((MemInstanceData)memb).Type.Reference is TypPasProperty)
				{
					fieldsType.SubFields.RemoveAt(i);
					continue;
				}
				memb.Visit(this);
				i++;
			}
		}

		public void Visit(TypBitField bitField)
		{
			bitField.Type = Add(bitField.Type);
		}

		public void Visit(TypMList mlistType)
		{
			foreach (MListItem item in mlistType.Methods)
				Add(item.Func);
		}

		public void Visit(MemRealBaseClass realbase)
		{
			Add(realbase.Class);
		}

		public void Visit(MemVirtualBaseClass virtbase)
		{
			virtbase.Class = Add(virtbase.Class);
			virtbase.PtrType = Add(virtbase.PtrType);
		}

		public void Visit(MemEnumerate memenum)
		{
		}

		public void Visit(MemIndex index)
		{
			index.Continuation = Add(index.Continuation);
		}

		public void Visit(MemInstanceData instdata)
		{
			Add(instdata.Type);
		}

		public void Visit(MemStaticData statdata)
		{
			statdata.Type = Add(statdata.Type);
		}

		public void Visit(MemMethod method)
		{
			method.MethodType = (TypMFunction)Add(method.MethodType);
		}

		public void Visit(MemMethods methods)
		{
			Add(methods.List);
		}

		public void Visit(MemNestedType nesttype)
		{
			Add(nesttype.NestedType);
		}

		public void Visit(MemVtabPointer vtabptr)
		{
			Add(vtabptr.VtabShapePtr);
		}
	}


	class ConsideredLabel { }


	class TypeListsMaker : ISymbolVisitor
	{
		private List<TypeInfo> m_types;
		private TypeListsMakerTyp m_typeVis;
		private int m_closureId = 0;
		private TypPointer m_closurevoidptr = null;

		public TypeListsMaker(List<TypeInfo> types)
		{
			m_types = types;
			m_typeVis = new TypeListsMakerTyp(this);
		}

		internal TypeInfo Add(TypeInfo type)
		{
			if (type == null)
				return null;
			if (type is PrimitiveTypeInfo)
				return type;
			if (type is TypMList && ((TypMList)type).Methods.Count == 1)
				throw new ApplicationException("Invalid type: TypMList with methods count = 1");
			if (type is TypPasClosure)
			{
				TypPasClosure closure = (TypPasClosure)type;
				TypProcedure proc = new TypProcedure();
				proc.ArgsRef.Ref = closure.ArgsRef.Ref;
				proc.CallConv = closure.CallConv;
				proc.RetType.Ref = closure.RetType.Ref;
				TypPointer procptr = new TypPointer();
				procptr.PointedType.Ref = proc;
				procptr.Attribute.Mode = PointerMode.Pointer;
				procptr.Attribute.PtrType = PointerType.Near32;
				if (m_closurevoidptr == null)
				{
					m_closurevoidptr = new TypPointer();
					m_closurevoidptr.PointedType.Ref = Program.TypeInfoFromId(3);
					m_closurevoidptr.Attribute.Mode = PointerMode.Pointer;
					m_closurevoidptr.Attribute.PtrType = PointerType.Near32;
				}
				MemInstanceData procptrfield = new MemInstanceData();
				procptrfield.Name = "Member";
				procptrfield.Offset.Assign((ushort)0);
				procptrfield.Type.Ref = procptr;
				procptrfield.Attributes.Access = AccessType.Public;
				MemInstanceData objptrfield = new MemInstanceData();
				objptrfield.Name = "Object";
				objptrfield.Offset.Assign((ushort)4);
				objptrfield.Type.Ref = m_closurevoidptr;
				objptrfield.Attributes.Access = AccessType.Public;
				TypMembersList members = new TypMembersList();
				members.SubFields.Add(procptrfield);
				members.SubFields.Add(objptrfield);
				TypStruct closureStruct = new TypStruct();
				closureStruct.Name = "__closure" + m_closureId.ToString();
				m_closureId++;
				closureStruct.IsClass = false;
				closureStruct.Size.Assign((ushort)8);
				closureStruct.Members.Ref = members;
				return Add(closureStruct);
			}
			if (type.Tag != null && type.Tag is int)
				return type;
			if (type.Tag == null)
			{
				type.Tag = new ConsideredLabel();
				type.Visit(m_typeVis);
			}
			m_types.Add(type);
			type.Tag = m_types.Count - 1;
			return type;
		}

		public void Visit(SymRegister reg)
		{
			reg.Type = Add(reg.Type);
		}

		public void Visit(SymConstant symConst)
		{
			symConst.DataType = Add(symConst.DataType);
		}

		public void Visit(SymUdt udt)
		{
			udt.Type = Add(udt.Type);
		}

		public void Visit(SymGProcRef procRef)
		{
			procRef.ProcType = Add(procRef.ProcType);
		}

		public void Visit(SymGDataRef dataRef)
		{
			dataRef.DataType = Add(dataRef.DataType);
		}

		public void Visit(SymBpRelative32Info bprel)
		{
			bprel.DataType = Add(bprel.DataType);
		}

		public void Visit(SymData32 symData)
		{
			symData.DataType = Add(symData.DataType);
		}

		public void Visit(SymProc32Info procInfo)
		{
			procInfo.ProcType = Add(procInfo.ProcType);
		}

		public void Visit(SymCompile comp) { }
		public void Visit(SymSearch search) { }
		public void Visit(SymEnd end) { }
		public void Visit(SymThunk32 thunk32) { }
		public void Visit(SymBlock32 block32) { }
		public void Visit(SymWith32 with32) { }
		public void Visit(SymLabel32 label) { }
		public void Visit(SymEntry32 entry) { }
		public void Visit(SymOptVar32 optvar) { }
		public void Visit(SymProcRet32 symProcRet) { }
		public void Visit(SymSaveRegs32 saveregs) { }
		public void Visit(SymUses symUses) { }
		public void Visit(SymNamespace symNamespace) { }
		public void Visit(SymUsing symUsing) { }
		public void Visit(SymPConstant pconstant) { }
		public void Visit(SymSLink32 pconstant) { }
	}


	class SectionContribution : IComparable<SectionContribution>
	{
		public ushort SectionIndex;
		public int Offset;
		public int Length;
		public ushort ModuleIndex;


		public int CompareTo(SectionContribution other)
		{
			if (SectionIndex > other.SectionIndex)
				return 1;
			else if (SectionIndex < other.SectionIndex)
				return -1;
			if (Offset > other.Offset)
				return 1;
			else if (Offset < other.Offset)
				return -1;
			return 0;
		}
	}


	class PdbWriter
	{
		internal const short CV_LF_MODIFIER = 0x1001;
		internal const short CV_LF_POINTER = 0x1002;
		internal const short CV_LF_PROCEDURE = 0x1008;
		internal const short CV_LF_MFUNCTION = 0x1009;
		internal const short CV_LF_VTSHAPE = 0xA;
		internal const short CV_LF_ARGLIST = 0x1201;
		internal const short CV_LF_LIST = 0x1203;
		internal const short CV_LF_BITFIELD = 0x1205;
		internal const short CV_LF_MLIST = 0x1206;
		internal const short CV_LF_BCLASS = 0x1400;
		internal const short CV_LF_VBCLASS = 0x1401;
		internal const short CV_LF_IVBCLASS = 0x1402;
		internal const short CV_LF_INDEX = 0x1404;
		internal const short CV_LF_ENUMERATE = 0x1502;
		internal const short CV_LF_ARRAY = 0x1503;
		internal const short CV_LF_CLASS = 0x1504;
		internal const short CV_LF_STRUCTURE = 0x1505;
		internal const short CV_LF_UNION = 0x1506;
		internal const short CV_LF_ENUM = 0x1507;
		internal const short CV_LF_MEMBER = 0x150D;
		internal const short CV_LF_STMEMBER = 0x150E;
		internal const short CV_LF_METHOD = 0x150F;
		internal const short CV_LF_NESTEDTYPE = 0x1510;
		internal const short CV_LF_VFUNCTAB = 0x1409;
		internal const short CV_LF_VFUNCOFF = 0x140A;
		internal const short CV_LF_ONEMETHOD = 0x1511;
		internal const short CV_S_END = 6;
		internal const short CV_S_THUNK32 = 0x1102;
		internal const short CV_S_BLOCK32 = 0x1103;
		internal const short CV_S_CONSTANT = 0x1107;
		internal const short CV_S_UDT = 0x1108;
		internal const short CV_S_BPREL32 = 0x110B;
		internal const short CV_S_LDATA32 = 0x110C;
		internal const short CV_S_GDATA32 = 0x110D;
		internal const short CV_S_PUBLIC32 = 0x110E;
		internal const short CV_S_LPROC32 = 0x110F;
		internal const short CV_S_GPROC32 = 0x1110;
		internal const short CV_S_COMPILE = 0x1116;
		internal const short CV_S_GPROCREF = 0x1125;
		internal static List<TypeInfo> m_types = new List<TypeInfo>();
		internal static List<MemoryStream> m_streams = new List<MemoryStream>();
		internal static readonly byte[] pad = { 0, 0, 0, 0 };
		internal static ModuleInfo[] m_modules;

		internal static void WriteStructFlags(BinaryWriter wrr, TdsStructFlags flags)
		{
			StructFlags val = 0;
			if ((flags & TdsStructFlags.Packed) != 0)
				val |= StructFlags.Packed;
			if ((flags & (TdsStructFlags.Ctor | TdsStructFlags.Dtor)) != 0)
				val |= StructFlags.CtorDtor;
			if ((flags & TdsStructFlags.OverOpers) != 0)
				val |= StructFlags.OverOpers;
			if ((flags & TdsStructFlags.IsNested) != 0)
				val |= StructFlags.IsNested;
			if ((flags & TdsStructFlags.CNested) != 0)
				val |= StructFlags.CNested;
			if ((flags & TdsStructFlags.OpAssign) != 0)
				val |= StructFlags.OpAssign;
			if ((flags & TdsStructFlags.OpCast) != 0)
				val |= StructFlags.OpCast;
			if ((flags & TdsStructFlags.FwdRef) != 0)
				val |= StructFlags.FwdRef;
			wrr.Write((ushort)val);
		}

		internal static void WriteCallConv(BinaryWriter wrr, TdsCallConv callConv)
		{
			wrr.Write((byte)callConv.Kind);
		}

		private static int GetCTime(DateTime when)
		{
			DateTime start = new DateTime(1970, 1, 1);
			TimeSpan span = DateTime.Now - start;
			int seconds = (int)span.TotalSeconds;
			return seconds;
		}

		internal static int TypeInfoToId(TypeInfo type)
		{
			if (type == null)
				return 0;
			PrimitiveTypeInfo primType = type as PrimitiveTypeInfo;
			if (primType != null)
			{
				return (int)primType.PrimitiveType;
			}
			TypPasClosure closure = type as TypPasClosure;
			if (closure != null)
				type = closure.Replacement;
			return (int)type.Tag + 0x1000;
		}

		internal static void WriteUTF8Z(Stream stm, string str)
		{
			if (str != null)
			{
				byte[] buffer = Encoding.UTF8.GetBytes(str);
				stm.Write(buffer, 0, buffer.Length);
			}
			stm.WriteByte(0); // zero terminator
		}

		internal static void WriteUTF8Z(BinaryWriter wrr, string str)
		{
			WriteUTF8Z(wrr.BaseStream, str);
		}

		private static int Align(int val)
		{
			return (val + 3) / 4 * 4;
		}

		private static void NewStream(out MemoryStream stm)
		{
			short stmNo;
			NewStream(out stm, out stmNo);
		}

		private static void NewStream(out MemoryStream stm, out short stmNo)
		{
			stm = new MemoryStream();
			m_streams.Add(stm);
			stmNo = (short)m_streams.Count;
		}

		internal static void WriteMemberAttributes(BinaryWriter wrr, TdsMemberAttribute attribs)
		{
			int validAttribs = (int)(attribs.Attributes & (TdsMemberAttributes.NeverInstantuated |
				TdsMemberAttributes.NoInherit | TdsMemberAttributes.NoConstruct));

			int val = 0;
			val = (int)attribs.Access | (((int)attribs.MProp) << 2) | (validAttribs << 5);
			wrr.Write((short)val);
		}

		public static void WritePdb(List<SymbolInfo> tglobals, ModuleInfo[] modules, string pdbname, string exename)
		{
			// preprocessing types
			m_types.Clear();
			TypeListsMaker typeListMaker = new TypeListsMaker(m_types);
			foreach (ModuleInfo mod in modules)
			{
				foreach (SymbolInfo sym in mod.Symbols)
					sym.Visit(typeListMaker);
			}
			//am foreach (SymbolInfo sym in globals)
			//  	sym.Visit(typeListMaker);
			m_modules = modules;

			BinaryWriter wrr;
			MemoryStream infoStream;
			int timestamp = GetCTime(DateTime.Now);
			int age = 1;
			NewStream(out infoStream);
			wrr = new BinaryWriter(infoStream, Encoding.ASCII);
			wrr.Write((int)20000404);
			wrr.Write(timestamp);
			wrr.Write(age); // version
			Guid guid = Guid.NewGuid();
			wrr.Write(guid.ToByteArray(), 0, 16);
			// NMTNI
			wrr.Write((int)0); // class:pdb_internal::Buffer::size, obj:NMTNI+112
			// class:pdb_internal::Map<ulong,ulong,pdb_internal::HashClass<ulong,0>,void,CriticalSectionNop>, obj:NMTNI+0
				wrr.Write((int)0); // field:this+48
				wrr.Write((int)0x10); // hashSize
				wrr.Write((int)0); // array len, obj:Map+24
				wrr.Write((int)0); // array len, obj:Map+36
			wrr.Write((int)0); // field:NMTNI+144

			MemoryStream typesStream;
			MemoryStream modulesStream;
			NewStream(out typesStream);
			NewStream(out modulesStream);
			short autTypesStmNo;
			MemoryStream auxTypesStream;
			NewStream(out auxTypesStream, out autTypesStmNo);
			wrr = new BinaryWriter(typesStream);
			wrr.Write((int)20040203);
			wrr.Write((int)14*4); // header size
			wrr.Write((int)0x1000); // first type no
			wrr.Write((int)(0x1000 + m_types.Count));
			int typesSizePos = (int)typesStream.Position;
			wrr.Write((int)0); // types size
			wrr.Write((short)autTypesStmNo); // types hash
			wrr.Write((short)-1);
			wrr.Write((int)4); // hash size
			uint typesHashSize = 0x8003;
			wrr.Write((int)typesHashSize); // hash table size
			wrr.Write((int)0); // hashes offset
			wrr.Write((int)(4 * m_types.Count)); // hashes size
			wrr.Write((int)(4 * m_types.Count)); // block ranges offset
			wrr.Write((int)8); // block ranges length
			wrr.Write((int)(4 * m_types.Count + 8)); // unknown
			wrr.Write((int)0); // unknown
			PdbWriterVisitor visitor = new PdbWriterVisitor();
			visitor.wrr = wrr;
			int typesStart = (int)typesStream.Position;
			int alignedTypeEnd = typesStart;
			foreach (TypeInfo ti in m_types)
			{
				int typeLenPos = (int)typesStream.Position;
				wrr.Write((short)0);
				int typeStart = (int)typesStream.Position;
				ti.Visit(visitor);
				AlignTypes4(wrr);
				alignedTypeEnd = (int)typesStream.Position;
				short typeLen = (short)(alignedTypeEnd - typeStart);
				typesStream.Position = typeLenPos;
				wrr.Write(typeLen);
				typesStream.Position = alignedTypeEnd;

				// writing hash
				TypStruct struc = ti as TypStruct;
				TypEnum typenum = ti as TypEnum;
				TypUnion union = ti as TypUnion;
				string name = null;
				if (struc != null && (struc.Flags & (TdsStructFlags.FwdRef /*| StructFlags.Scoped*/ )) == 0)
				{
					name = struc.Name;
				}
				else if (typenum != null)
				{
					name = typenum.Name;
				}
				else if (union != null && (union.Flags & (TdsStructFlags.FwdRef /*| StructFlags.Scoped*/)) == 0)
				{
					name = union.Name;
				}
				uint hash;
				if (name != null)
				{
					hash = Hash(name, typesHashSize);
				}
				else
				{
					hash = Sig(typesStream.GetBuffer(), typeLenPos, alignedTypeEnd - typeLenPos, 0) % typesHashSize;
				}
				auxTypesStream.Write(BitConverter.GetBytes(hash), 0, 4);
			}
			typesStream.Position = typesSizePos;
			int typesSize = alignedTypeEnd - typesStart;
			wrr.Write(typesSize);

			wrr = new BinaryWriter(auxTypesStream);
			wrr.Write((int)0x1000);
			wrr.Write((int)0);

			MemoryStream globalsIdxStm;
			MemoryStream globPubSymsStm;
			short globIdxStmNo;
			short globPubSymsStmNo;
			NewStream(out globalsIdxStm, out globIdxStmNo);
			NewStream(out globPubSymsStm, out globPubSymsStmNo);

			wrr = new BinaryWriter(modulesStream);
			wrr.Write((int)-1);
			wrr.Write((int)19970606); // версия
			wrr.Write(age); // file version
			wrr.Write((short)globIdxStmNo); // globals index stream
			wrr.Write((short)0); // unknown
			wrr.Write((short)-1); // publics index stream
			wrr.Write((short)0); // unknown
			wrr.Write((short)globPubSymsStmNo); // global/public symbols
			wrr.Write((short)0); // unknown
			int modulesLenPos = (int)modulesStream.Position;
			wrr.Write((int)0); // длина модулей, записать потом
			int secContribsLenPos = (int)modulesStream.Position;
			wrr.Write((int)0); // seccontribs len, write later
			int secInfoLenPos = (int)modulesStream.Position;
			wrr.Write((int)0); // sections infos len, write later
			int fileInfoLenPos = (int)modulesStream.Position;
			wrr.Write((int)0); // file infos len
			wrr.Write((int)0); // unknown
			wrr.Write((int)0); // unknown
			wrr.Write((int)0); // unknown
			wrr.Write((int)0); // unknown
			wrr.Write((short)0); // flags
			wrr.Write((short)0x014c); // machine - x86
			wrr.Write((int)0); // unknown
			int modulesStart = (int)modulesStream.Position;
			int alignedModuleEnd = modulesStart;
			List<uint> endAddr = new List<uint>();
			List<ushort> modStarts = new List<ushort>();
			List<ushort> modNums = new List<ushort>();
			List<uint> fileInfoOffsets = new List<uint>();
			MemoryStream fileInfoStringsStm = new MemoryStream();
			SortedList<SectionContribution, object> secContribs = new SortedList<SectionContribution, object>();
			for (int moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
			{
				ModuleInfo mod = modules[moduleIndex];
				modStarts.Add((ushort)fileInfoOffsets.Count);
				if (mod.Sources != null)
				{
					modNums.Add((ushort)mod.Sources.SourceFiles.Count);
					foreach (SourceFileInfo srcFile in mod.Sources.SourceFiles)
					{
						fileInfoOffsets.Add((uint)fileInfoStringsStm.Position);
						WriteUTF8Z(fileInfoStringsStm, srcFile.Name);
					}
				}
				else
				{
					modNums.Add(0);
				}
				foreach (SegInfo segInfo in mod.CodeSegs)
				{
					int secIndex = segInfo.SegIndex;
					while (endAddr.Count < secIndex)
						endAddr.Add(0xffffffff);
					if (endAddr[secIndex - 1] == 0xffffffff)
						endAddr[secIndex - 1] = (uint)segInfo.Offset + (uint)segInfo.Length;
					else
						endAddr[secIndex - 1] = (uint)Math.Max(endAddr[secIndex - 1], segInfo.Offset + segInfo.Length);

					SectionContribution sc = new SectionContribution();
					sc.SectionIndex = (ushort)segInfo.SegIndex;
					sc.Offset = segInfo.Offset;
					sc.Length = segInfo.Length;
					sc.ModuleIndex = (ushort)moduleIndex;
					secContribs.Add(sc, null);
				}
				int moduleStart = (int)modulesStream.Position;
				wrr.Write((int)0); // unknown
                if (mod.CodeSegs.Length > 0)
				  wrr.Write((short)mod.CodeSegs[0].SegIndex); // segment
                else
  				  wrr.Write((short)0); 
				wrr.Write((short)0); // unknown
                if (mod.CodeSegs.Length > 0)
				  wrr.Write((int)mod.CodeSegs[0].Offset); // offset
                else
                    wrr.Write((short)0);
                if (mod.CodeSegs.Length > 0)
                    wrr.Write((int)mod.CodeSegs[0].Length); // code size
                else
  				  wrr.Write((short)0); 
				wrr.Write((int)0); // characteristics
				wrr.Write((short)0); // subsection
				wrr.Write((short)0); // unknown
				wrr.Write((int)0); // checksum
				wrr.Write((short)0); // unknown
				wrr.Write((short)0); // unknown
				wrr.Write((short)0); // unknown
				MemoryStream moduleStream;
				short streamNo = -1;
				int symbolsLen = 0;
				int sourcesLen = 0;
				if ((mod.Symbols != null && mod.Symbols.Count != 0) ||
					mod.Sources != null)
				{
					NewStream(out moduleStream, out streamNo);
					WriteModuleStream(moduleStream, mod, out symbolsLen, out sourcesLen);
				}
				wrr.Write(streamNo);
				wrr.Write(symbolsLen); // длина символов, записать потом
				wrr.Write(sourcesLen); // длина номеров строк, записать потом
				wrr.Write((int)0); // длина номеров строк v2, не используем
				int sourcesCount = 0;
				if (mod.Sources != null)
					sourcesCount = mod.Sources.SourceFiles.Count;
				wrr.Write((ushort)sourcesCount);
				wrr.Write((short)0);
				wrr.Write((int)0); // unknown
				wrr.Write((int)0); // unknown
				wrr.Write((int)0); // unknown
				WriteUTF8Z(wrr, mod.Name);
				WriteUTF8Z(wrr, mod.Name);
				int moduleEnd = (int)modulesStream.Position;
				alignedModuleEnd = Align(moduleEnd);
				wrr.Write(pad, 0, alignedModuleEnd - moduleEnd);
			}
			modulesStream.Position = modulesLenPos;
			int modulesLen = alignedModuleEnd - modulesStart;
			wrr.Write(modulesLen);
			modulesStream.Position = alignedModuleEnd;

			int secContribsBegin = (int)modulesStream.Position;
			wrr.Write((uint)0xf12eba2d); // signature
			foreach (SectionContribution sc in secContribs.Keys)
			{
				wrr.Write((ushort)sc.SectionIndex);
				wrr.Write((short)0); // padding
				wrr.Write((int)sc.Offset);
				wrr.Write((int)sc.Length);
				wrr.Write((uint)0); // characteristics
				wrr.Write((ushort)sc.ModuleIndex);
				wrr.Write((short)0); // padding
				wrr.Write((uint)0); // crc1
				wrr.Write((uint)0); // crc2
			}
			int secContribsEnd = (int)modulesStream.Position;
			modulesStream.Position = secContribsLenPos;
			wrr.Write(secContribsEnd - secContribsBegin);
			modulesStream.Position = secContribsEnd;

			// writing pdb sections mapping to image section
			int numSections = endAddr.Count;
			int secInfosBegin = (int)modulesStream.Position;
			wrr.Write((short)(numSections + 1));
			wrr.Write((short)(numSections + 1));
			for (int i = 0; i < endAddr.Count; i++)
			{
				wrr.Write((int)0); // unknown, can be 0x208 0x10B 0x109 0x10D, looks like flags
				wrr.Write((short)0); // unknown
				wrr.Write((ushort)(i + 1)); // image section index
				wrr.Write((int)-1); // unknown
				wrr.Write((uint)0); // offset
				wrr.Write((uint)endAddr[i]); // length
			}
			// last terminating section
			wrr.Write((int)0); // unknown, can be 0x208, looks like flags
			wrr.Write((short)0); // unknown
			wrr.Write((ushort)0); // image section index
			wrr.Write((int)-1); // unknown
			wrr.Write((uint)0); // offset
			wrr.Write((uint)0xffffffff); // length
			int secInfosEnd = (int)modulesStream.Position;
			modulesStream.Position = secInfoLenPos;
			wrr.Write(secInfosEnd - secInfosBegin);

			int fileInfosBegin = secInfosEnd;
			modulesStream.Position = fileInfosBegin;
			wrr.Write((ushort)modules.Length);
			wrr.Write((ushort)fileInfoOffsets.Count);
			foreach (ushort modstart in modStarts)
				wrr.Write(modstart);
			foreach (ushort modnum in modNums)
				wrr.Write(modnum);
			foreach (uint offset in fileInfoOffsets)
				wrr.Write(offset);
			wrr.Write(fileInfoStringsStm.GetBuffer(), 0, (int)fileInfoStringsStm.Length);
			int fileInfosEnd = (int)modulesStream.Position;
			modulesStream.Position = fileInfoLenPos;
			wrr.Write(fileInfosEnd - fileInfosBegin);

			// writing globals
            //am
            //BinaryWriter symwrr = new BinaryWriter(globPubSymsStm);
            //GlobalsHash globalsHash = new GlobalsHash();
            //foreach (SymbolInfo sym in globals)
            //{
            //    if (sym is SymData32)
            //    {
            //        globalsHash.Add(sym);
            //        WriteSymbol(symwrr, sym);
            //    }
            //    else if (sym is SymGProcRef)
            //    {
            //        SymGProcRef procref = sym as SymGProcRef;
            //        FindProcResult fr = FindReferencedProc(procref.Segment, procref.Offset);
            //        if (fr != null)				
            //        {
            //            globalsHash.Add(sym);
            //            WriteSymbol(symwrr, sym);
            //        }
            //    }
            //}
            //globalsHash.Serialize(globalsIdxStm);
            

			// rewind all streams
			foreach (Stream stm in m_streams)
				stm.Position = 0;

			FileStream outstm = File.OpenWrite(pdbname);
            //pdbcompile.  made internal, no dependency
            PdbCompiler.Compile(m_streams.ToArray(), outstm, 1024);
			//pdbbind.  made internal, no dependency
            PdbBind.Bind(exename, pdbname, timestamp, guid, age);
		}

		internal static void WriteModuleStream(MemoryStream stm, ModuleInfo mod, out int symbolsLen, out int sourcesLen)
		{
			BinaryWriter wrr = new BinaryWriter(stm);
			wrr.Write((int)4); // looks like offset to real symbols
			symbolsLen = WriteSymbols(stm, mod.Symbols) + 4;
			int sourcesStart = (int)stm.Position;
			SourcesInfo src = mod.Sources;
			if (src != null)
			{
				int sourcesBegin = (int)stm.Position;
				wrr.Write((short)src.SourceFiles.Count);
				wrr.Write((short)src.Ranges.Length);
				int fileOffsetsPos = (int)stm.Position;
				for (int i = 0; i < src.SourceFiles.Count; i++)
					wrr.Write((int)0);
				for (int i = 0; i < src.Ranges.Length; i++)
				{
					wrr.Write((int)src.Ranges[i].StartOffset);
					wrr.Write((int)src.Ranges[i].EndOffset);
				}
				for (int i = 0; i < src.Ranges.Length; i++)
					wrr.Write((short)src.Ranges[i].SegIndex);
				if (src.Ranges.Length % 2 == 1)
					wrr.Write((short)0); // padding
				for (int i = 0; i < src.SourceFiles.Count; i++)
				{
					SourceFileInfo srcfile = src.SourceFiles[i];
					int srcFilePos = (int)stm.Position;
					int srcFileOffset = srcFilePos - sourcesBegin;
					stm.Position = fileOffsetsPos + 4 * i;
					wrr.Write(srcFileOffset);
					stm.Position = srcFilePos;
					short numRanges = (short)srcfile.Ranges.Length;
					wrr.Write(numRanges);
					wrr.Write((short)0); // padding
					int rangesOffsetsPos = (int)stm.Position;
					for (int r = 0; r < numRanges; r++)
						wrr.Write((int)0);
					for (int r = 0; r < numRanges; r++)
					{
						wrr.Write((int)srcfile.Ranges[r].StartOffset);
						wrr.Write((int)srcfile.Ranges[r].EndOffset);
					}
					byte[] utf8srcfname = Encoding.UTF8.GetBytes(srcfile.Name);
					wrr.Write(utf8srcfname);
					wrr.Write((byte)0);
					int pads = (utf8srcfname.Length + 1) % 4;
					byte[] pad = { 0, 0, 0 };
					wrr.Write(pad, 0, pads);
					for (int r = 0; r < numRanges; r++)
					{
						SourceFileRangeInfo rng = srcfile.Ranges[r];
						int rangePos = (int)stm.Position;
						int rangeOffset = rangePos - sourcesBegin;
						stm.Position = rangesOffsetsPos + 4 * r;
						wrr.Write(rangeOffset);
						stm.Position = rangePos;
						wrr.Write((short)rng.SegIndex);
						short numLines = (short)rng.Lines.Length;
						wrr.Write(numLines);
						for (int l = 0; l < numLines; l++)
							wrr.Write((int)rng.Lines[l].Offset);
						for (int l = 0; l < numLines; l++)
							wrr.Write((short)rng.Lines[l].LineNo);
						if (numLines % 2 == 1)
							wrr.Write((short)0); // padding
					}
				}
			}
			int sourcesEnd = (int)stm.Position;
			sourcesLen = sourcesEnd - sourcesStart;
		}

		private static bool WriteSymbol(BinaryWriter wrr, SymbolInfo sym)
		{
			if (sym is SymSearch || sym is SymSaveRegs32 || sym is SymUsing ||
				sym is SymNamespace || sym is SymProcRet32 || sym is SymOptVar32 ||
				sym is SymEntry32 || sym is SymGDataRef || sym is SymRegister ||
				sym is SymLabel32)
			{
				return false;
			}
			PdbWriterVisitor visitor = new PdbWriterVisitor();
			visitor.wrr = wrr;
			int lenPos = (int)wrr.BaseStream.Position;
			wrr.Write((short)0); // placeholder for symbol length
			int symStart = (int)wrr.BaseStream.Position;
			sym.Visit(visitor);
			int symEnd = (int)wrr.BaseStream.Position;
			int alignedSymEnd = Align(symEnd);
			int pads = alignedSymEnd - symEnd;
			wrr.Write(pad, 0, pads);
			wrr.BaseStream.Position = lenPos;
			short symLen = (short)(alignedSymEnd - symStart);
			wrr.Write(symLen);
			wrr.BaseStream.Position = alignedSymEnd;
			sym.Tag = lenPos;
			return true;
		}

		private static int WriteSymbols(MemoryStream stm, ICollection<SymbolInfo> symbols)
		{
			BinaryWriter wrr = new BinaryWriter(stm);
			PdbWriterVisitor visitor = new PdbWriterVisitor();
			visitor.wrr = wrr;
			int symbolsStart = (int)stm.Position;
			foreach (SymbolInfo sym in symbols)
			{
				WriteSymbol(wrr, sym);
			}
			int symbolsEnd = (int)stm.Position;
			foreach (SymbolInfo sym in symbols)
			{
				SymScopedInfo scoped = sym as SymScopedInfo;
				if (scoped == null)
					continue;
				int thisOffset = (int)sym.Tag;
				const int parentFieldOffset = 4;
				const int endFieldOffset = 8;
				const int nextFieldOffset = 12;
				int parentOffset = 0;
				int endOffset = 0;
				int nextOffset = 0;
				if (scoped.Parent != null)
					parentOffset = (int)scoped.Parent.Tag;
				if (scoped.End != null)
					endOffset = (int)scoped.End.Tag;
				if (scoped.Next != null)
					nextOffset = (int)scoped.Next.Tag;
				if (parentOffset != 0)
				{
					stm.Position = thisOffset + parentFieldOffset;
					wrr.Write(parentOffset);
				}
				if (endOffset != 0)
				{
					stm.Position = thisOffset + endFieldOffset;
					wrr.Write(endOffset);
				}
				if (nextOffset != 0)
				{
					stm.Position = thisOffset + nextFieldOffset;
					wrr.Write(nextOffset);
				}
			}
			stm.Position = symbolsEnd;
			return symbolsEnd - symbolsStart;
		}

		internal static void Align4(BinaryWriter wrr)
		{
			long pos = wrr.BaseStream.Position;
			if (pos % 4 == 0)
				return;
			int pads = (int)(4 - pos % 4);
			byte[] pad = { 0, 0, 0 };
			wrr.Write(pad, 0, pads);
		}

		internal static void AlignTypes4(BinaryWriter wrr)
		{
			long pos = wrr.BaseStream.Position;
			if (pos % 4 == 0)
				return;
			int pads = (int)(4 - pos % 4);
			for (; pads > 0; pads--)
				wrr.Write((byte)(0xf0 | pads));
		}

		internal static uint Hash(string mystr, uint modulo)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(mystr);
			uint hash = 0;
			int i = 0;
			for (i = 0; i / 4 < bytes.Length / 4; i += 4)
			{
				hash ^= (uint)bytes[i] | (uint)bytes[i + 1] << 8 |
					(uint)bytes[i + 2] << 16 | (uint)bytes[i + 3] << 24;
			}
			if ((bytes.Length & 2) != 0)
			{
				hash ^= (uint)bytes[i] | (uint)bytes[i + 1] << 8;
				i += 2;
			}
			if ((bytes.Length & 1) != 0)
			{
				hash ^= (uint)bytes[i];
			}
			hash |= 0x20202020;
			hash ^= hash >> 11;
			hash ^= hash >> 16;
			return hash % modulo;
		}

		internal static uint Sig(byte[] buffer, int offset, int length, byte seed)
		{
			uint[] table = { 0,
				0x77073096, 0x0EE0E612C, 0x990951BA, 0x76DC419, 0x706AF48F,
				0x0E963A535, 0x9E6495A3, 0x0EDB8832, 0x79DCB8A4, 0x0E0D5E91E,
				0x97D2D988, 0x9B64C2B, 0x7EB17CBD, 0x0E7B82D07, 0x90BF1D91,
				0x1DB71064, 0x6AB020F2, 0x0F3B97148, 0x84BE41DE, 0x1ADAD47D,
				0x6DDDE4EB, 0x0F4D4B551, 0x83D385C7, 0x136C9856, 0x646BA8C0,
				0x0FD62F97A, 0x8A65C9EC, 0x14015C4F, 0x63066CD9, 0x0FA0F3D63,
				0x8D080DF5, 0x3B6E20C8, 0x4C69105E, 0x0D56041E4, 0x0A2677172,
				0x3C03E4D1, 0x4B04D447, 0x0D20D85FD, 0x0A50AB56B, 0x35B5A8FA,
				0x42B2986C, 0x0DBBBC9D6, 0x0ACBCF940, 0x32D86CE3, 0x45DF5C75,
				0x0DCD60DCF, 0x0ABD13D59, 0x26D930AC, 0x51DE003A, 0x0C8D75180,
				0x0BFD06116, 0x21B4F4B5, 0x56B3C423, 0x0CFBA9599, 0x0B8BDA50F,
				0x2802B89E, 0x5F058808, 0x0C60CD9B2, 0x0B10BE924, 0x2F6F7C87,
				0x58684C11, 0x0C1611DAB, 0x0B6662D3D, 0x76DC4190, 0x1DB7106,
				0x98D220BC, 0x0EFD5102A, 0x71B18589, 0x6B6B51F, 0x9FBFE4A5,
				0x0E8B8D433, 0x7807C9A2, 0x0F00F934, 0x9609A88E, 0x0E10E9818,
				0x7F6A0DBB, 0x86D3D2D, 0x91646C97, 0x0E6635C01, 0x6B6B51F4,
				0x1C6C6162, 0x856530D8, 0x0F262004E, 0x6C0695ED, 0x1B01A57B,
				0x8208F4C1, 0x0F50FC457, 0x65B0D9C6, 0x12B7E950, 0x8BBEB8EA,
				0x0FCB9887C, 0x62DD1DDF, 0x15DA2D49, 0x8CD37CF3, 0x0FBD44C65,
				0x4DB26158, 0x3AB551CE, 0x0A3BC0074, 0x0D4BB30E2, 0x4ADFA541,
				0x3DD895D7, 0x0A4D1C46D, 0x0D3D6F4FB, 0x4369E96A, 0x346ED9FC,
				0x0AD678846, 0x0DA60B8D0, 0x44042D73, 0x33031DE5, 0x0AA0A4C5F,
				0x0DD0D7CC9, 0x5005713C, 0x270241AA, 0x0BE0B1010, 0x0C90C2086,
				0x5768B525, 0x206F85B3, 0x0B966D409, 0x0CE61E49F, 0x5EDEF90E,
				0x29D9C998, 0x0B0D09822, 0x0C7D7A8B4, 0x59B33D17, 0x2EB40D81,
				0x0B7BD5C3B, 0x0C0BA6CAD, 0x0EDB88320, 0x9ABFB3B6, 0x3B6E20C,
				0x74B1D29A, 0x0EAD54739, 0x9DD277AF, 0x4DB2615, 0x73DC1683,
				0x0E3630B12, 0x94643B84, 0x0D6D6A3E, 0x7A6A5AA8, 0x0E40ECF0B,
				0x9309FF9D, 0x0A00AE27, 0x7D079EB1, 0x0F00F9344, 0x8708A3D2,
				0x1E01F268, 0x6906C2FE, 0x0F762575D, 0x806567CB, 0x196C3671,
				0x6E6B06E7, 0x0FED41B76, 0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC,
				0x0F9B9DF6F, 0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5, 0x0D6D6A3E8,
				0x0A1D1937E, 0x38D8C2C4, 0x4FDFF252, 0x0D1BB67F1, 0x0A6BC5767,
				0x3FB506DD, 0x48B2364B, 0x0D80D2BDA, 0x0AF0A1B4C, 0x36034AF6,
				0x41047A60, 0x0DF60EFC3, 0x0A867DF55, 0x316E8EEF, 0x4669BE79,
				0x0CB61B38C, 0x0BC66831A, 0x256FD2A0, 0x5268E236, 0x0CC0C7795,
				0x0BB0B4703, 0x220216B9, 0x5505262F, 0x0C5BA3BBE, 0x0B2BD0B28,
				0x2BB45A92, 0x5CB36A04, 0x0C2D7FFA7, 0x0B5D0CF31, 0x2CD99E8B,
				0x5BDEAE1D, 0x9B64C2B0, 0x0EC63F226, 0x756AA39C, 0x26D930A,
				0x9C0906A9, 0x0EB0E363F, 0x72076785, 0x5005713, 0x95BF4A82,
				0x0E2B87A14, 0x7BB12BAE, 0x0CB61B38, 0x92D28E9B, 0x0E5D5BE0D,
				0x7CDCEFB7, 0x0BDBDF21, 0x86D3D2D4, 0x0F1D4E242, 0x68DDB3F8,
				0x1FDA836E, 0x81BE16CD, 0x0F6B9265B, 0x6FB077E1, 0x18B74777,
				0x88085AE6, 0x0FF0F6A70, 0x66063BCA, 0x11010B5C, 0x8F659EFF,
				0x0F862AE69, 0x616BFFD3, 0x166CCF45, 0x0A00AE278, 0x0D70DD2EE,
				0x4E048354, 0x3903B3C2, 0x0A7672661, 0x0D06016F7, 0x4969474D,
				0x3E6E77DB, 0x0AED16A4A, 0x0D9D65ADC, 0x40DF0B66, 0x37D83BF0,
				0x0A9BCAE53, 0x0DEBB9EC5, 0x47B2CF7F, 0x30B5FFE9, 0x0BDBDF21C,
				0x0CABAC28A, 0x53B39330, 0x24B4A3A6, 0x0BAD03605, 0x0CDD70693,
				0x54DE5729, 0x23D967BF, 0x0B3667A2E, 0x0C4614AB8, 0x5D681B02,
				0x2A6F2B94, 0x0B40BBE37, 0x0C30C8EA1, 0x5A05DF1B, 0x2D02EF8D,
				0x75667608, 0x6174636E, 0x62 };

			uint hash = seed;
			for (int i = offset; i < Math.Min(offset + length, buffer.Length); i++)
				hash = table[buffer[i] ^ (hash & 0xff)] ^ (hash >> 8);
			return hash;
		}

		public class FindProcResult
		{
			public SymProc32Info Proc;
			public int ModuleIndex;
		}

		public static FindProcResult FindReferencedProc(int isect, int offset)
		{
			for (int i = 0; i < m_modules.Length; i++)
			{
				ModuleInfo mod = m_modules[i];
				foreach (SegInfo seg in mod.CodeSegs)
				{
					if (seg.SegIndex == isect && offset >= seg.Offset && offset < seg.Offset + seg.Length)
					{
						foreach (SymbolInfo sym in mod.Symbols)
						{
							if (sym is SymProc32Info)
							{
								SymProc32Info proc = sym as SymProc32Info;
								if (proc.IsGlobal && proc.Offset == offset && proc.Segment == isect)
								{
									FindProcResult result = new FindProcResult();
									result.Proc = proc;
									result.ModuleIndex = i;
									return result;
								}
							}
						}
						return null;
					}
				}
			}
			return null;
		}

	}
}