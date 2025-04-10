﻿using ScubaDiver.Rtti;
using System.Linq;

namespace ScubaDiver;

public class UndecoratedInternalFunction : UndecoratedFunction
{
    private ModuleInfo _module;
    private nuint _address;

    public override nuint Address => _address;
    public override ModuleInfo Module => _module;

    private string[] _argTypes;
    public override string[] ArgTypes => _argTypes;

    private string _retType;
    public override string RetType => _retType;


    public UndecoratedInternalFunction(
        ModuleInfo moduleInfo,
        string undecoratedName,
        string undecoratedFullName,
        string decoratedName,
        nuint address,
        int numArgs,
        string retType)
        : base(decoratedName, undecoratedName, undecoratedFullName, numArgs)
    {
        _address = address;
        _module = moduleInfo;

        _argTypes = Enumerable.Repeat("long", numArgs).ToArray();
        _retType = retType;
    }
}