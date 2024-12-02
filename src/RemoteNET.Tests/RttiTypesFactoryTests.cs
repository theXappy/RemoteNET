using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NtApiDotNet.Win32;
using RemoteNET.Common;
using RemoteNET.RttiReflection;
using ScubaDiver;
using ScubaDiver.API;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.Rtti;

namespace RemoteNET.Tests
{
    [TestFixture]
    public class RttiTypesFactoryTests
    {
        class FakeRemoteApp : RemoteApp
        {
            class FakeType : Type
            {
                public override object[] GetCustomAttributes(bool inherit)
                {
                    throw new NotImplementedException();
                }

                public override object[] GetCustomAttributes(Type attributeType, bool inherit)
                {
                    throw new NotImplementedException();
                }

                public override bool IsDefined(Type attributeType, bool inherit)
                {
                    throw new NotImplementedException();
                }

                public override Module Module { get; }
                public override string? Namespace { get; }
                public override string Name { get; }

                public FakeType(string namespacee, string name)
                {
                    Namespace = namespacee;
                    Name = name;
                }
                protected override TypeAttributes GetAttributeFlagsImpl()
                {
                    throw new NotImplementedException();
                }

                protected override ConstructorInfo? GetConstructorImpl(BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention,
                    Type[] types, ParameterModifier[]? modifiers)
                {
                    throw new NotImplementedException();
                }

                public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override Type? GetElementType()
                {
                    throw new NotImplementedException();
                }

                public override EventInfo? GetEvent(string name, BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override EventInfo[] GetEvents(BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override FieldInfo? GetField(string name, BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override FieldInfo[] GetFields(BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override MemberInfo[] GetMembers(BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                protected override MethodInfo? GetMethodImpl(string name, BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention,
                    Type[]? types, ParameterModifier[]? modifiers)
                {
                    throw new NotImplementedException();
                }

                public override MethodInfo[] GetMethods(BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override PropertyInfo[] GetProperties(BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override object? InvokeMember(string name, BindingFlags invokeAttr, Binder? binder, object? target, object?[]? args,
                    ParameterModifier[]? modifiers, CultureInfo? culture, string[]? namedParameters)
                {
                    throw new NotImplementedException();
                }

                public override Type UnderlyingSystemType { get; }

                protected override bool IsArrayImpl()
                {
                    throw new NotImplementedException();
                }

                protected override bool IsByRefImpl()
                {
                    throw new NotImplementedException();
                }

                protected override bool IsCOMObjectImpl()
                {
                    throw new NotImplementedException();
                }

                protected override bool IsPointerImpl()
                {
                    throw new NotImplementedException();
                }

                protected override bool IsPrimitiveImpl()
                {
                    throw new NotImplementedException();
                }

                public override Assembly Assembly { get; }
                public override string? AssemblyQualifiedName { get; }
                public override Type? BaseType { get; }
                public override string? FullName { get; }
                public override Guid GUID { get; }

                protected override PropertyInfo? GetPropertyImpl(string name, BindingFlags bindingAttr, Binder? binder, Type? returnType, Type[]? types,
                    ParameterModifier[]? modifiers)
                {
                    throw new NotImplementedException();
                }

                protected override bool HasElementTypeImpl()
                {
                    throw new NotImplementedException();
                }

                public override Type? GetNestedType(string name, BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override Type[] GetNestedTypes(BindingFlags bindingAttr)
                {
                    throw new NotImplementedException();
                }

                public override Type? GetInterface(string name, bool ignoreCase)
                {
                    throw new NotImplementedException();
                }

                public override Type[] GetInterfaces()
                {
                    throw new NotImplementedException();
                }
            }


            public override DiverCommunicator Communicator { get; }
            public override RemoteHookingManager HookingManager { get; }
            public override RemoteMarshal Marshal { get; }

            public override IEnumerable<CandidateType> QueryTypes(string typeFullNameFilter)
            {
                var cands = new CandidateType[]
                {
                    new CandidateType(RuntimeType.Unmanaged, "Peek::Child", "libPeek_lies.dll", 0x00000000_11223344),
                    new CandidateType(RuntimeType.Unmanaged, "Peek::Parent", "libPeek_lies.dll", 0x00000000_11223355)
                };

                return cands.Where(cand => cand.TypeFullName.Contains(typeFullNameFilter));
            }

            public override IEnumerable<CandidateObject> QueryInstances(string typeFullNameFilter, bool dumpHashcodes = true)
            {
                throw new NotImplementedException();

            }


            public override Type GetRemoteType(string typeFullName, string assembly = null)
            {
                var cands = new FakeType[]
                {
                    new FakeType("Peek", "Child"),
                    new FakeType("Peek", "Parent"),
                };

                return cands.Single(cand => $"{cand.Namespace}::{cand.Name}".Contains(typeFullName));
            }

            public override Type GetRemoteType(long methodTableAddress)
            {
                throw new NotImplementedException();
            }

            public override RemoteObject GetRemoteObject(ulong remoteAddress, string typeName, int? hashCode = null)
            {
                throw new NotImplementedException();
            }

            public override bool InjectAssembly(string path)
            {
                throw new NotImplementedException();
            }

            public override bool InjectDll(string path)
            {
                throw new NotImplementedException();
            }

            public override void Dispose()
            {
                throw new NotImplementedException();
            }
        }

        [Test]
        public void AddFunctionImpl_DifferentDeclaringClassOnFunc_DifferentDeclaringType()
        {
            // Arrange
            ScubaDiver.Rtti.ModuleInfo module = new ModuleInfo("libPeek_lies.dll", 0xaabbccdd, 0xaabbccdd);
            DllExport export = typeof(DllExport).GetConstructors((BindingFlags)0xffff)
                .Single(c => c.GetParameters().Length == 5).Invoke(new object?[]
                {
                    "?ApplyBinary@Parent@Peek@@UEAA_NPEAEIHMHHH@Z", 1, 0xbbccddee, "a", "b"
                }) as DllExport;
            export.TryUndecorate(module, out var undecFunc);
            TypeDump.TypeMethod? func = VftableParser.ConvertToTypeMethod(undecFunc as UndecoratedFunction);
            string childTypeLongName = "Peek::Child";
            TypeDump typeDump = new TypeDump()
            {
                Type = childTypeLongName,
                Assembly = "libPeek_lies.dll"
            };
            RemoteRttiType childType = new RemoteRttiType(null, childTypeLongName, "libPeek_lies.dll");
            RemoteApp fakeApp = new FakeRemoteApp();

            // Act
            RttiTypesFactory.AddFunctionImpl(fakeApp, typeDump, func, childType, false);

            // Assert
            MethodInfo method = childType.GetMethods().Single();
            string decTypeLongName = $"{method.DeclaringType.Namespace}::{method.DeclaringType.Name}";
            Assert.That(childTypeLongName, Is.EqualTo(decTypeLongName));
        }


        [Test]
        public void AddFunctionImpl_SameDeclaringClassOnFunc_SameDeclaringType()
        {
            // Arrange
            ScubaDiver.Rtti.ModuleInfo module = new ModuleInfo("libPeek_lies.dll", 0xaabbccdd, 0xaabbccdd);
            DllExport export = typeof(DllExport).GetConstructors((BindingFlags)0xffff)
                .Single(c => c.GetParameters().Length == 5).Invoke(new object?[]
                {
                    "?Construct@Child@Peek@@QEAA_NPEBVString@2@@Z", 1, 0xbbccddee, "a", "b"
                }) as DllExport;
            export.TryUndecorate(module, out var undecFunc);
            TypeDump.TypeMethod? func = VftableParser.ConvertToTypeMethod(undecFunc as UndecoratedFunction);
            string childTypeLongName = "Peek::Child";
            TypeDump typeDump = new TypeDump()
            {
                Type = childTypeLongName,
                Assembly = "libPeek_lies.dll"
            };
            RemoteRttiType childType = new RemoteRttiType(null, childTypeLongName, "libPeek_lies.dll");
            RemoteApp fakeApp = new FakeRemoteApp();

            // Act
            RttiTypesFactory.AddFunctionImpl(fakeApp, typeDump, func, childType, false);

            // Assert
            MethodInfo method = childType.GetMethods().Single();
            string decTypeLongName = $"{method.DeclaringType.Namespace}::{method.DeclaringType.Name}";
            Assert.That(childTypeLongName, Is.EqualTo(decTypeLongName));
            Assert.That(childType, Is.EqualTo(method.DeclaringType));
        }


        [Test]
        public void UndecoratingConstRef()
        {
            // Arrange
            ScubaDiver.Rtti.ModuleInfo module = new ModuleInfo("libPeek_lies.dll", 0xaabbccdd, 0xaabbccdd);
            DllExport export = typeof(DllExport).GetConstructors((BindingFlags)0xffff)
                .Single(c => c.GetParameters().Length == 5).Invoke(new object?[]
                {
                    "??0WClass@Peek@@QEAA@AEBV01@@Z", 1, 0xbbccddee, "a", "b"
                }) as DllExport;


            // Act
            export.TryUndecorate(module, out var undecFunc);
            var args = (undecFunc as UndecoratedExportedFunc).ArgTypes;

            // Assert
            var arg = args[1];
            Assert.That(arg, Is.EqualTo("Peek::WClass&"));
        }

        [Test]
        public void UndecoratingConstRef_ParseType_NoMethod()
        {
            // Arrange
            ScubaDiver.Rtti.ModuleInfo module = new ModuleInfo("libPeek_lies.dll", 0xaabbccdd, 0xaabbccdd);
            DllExport export = typeof(DllExport).GetConstructors((BindingFlags)0xffff)
                .Single(c => c.GetParameters().Length == 5).Invoke(new object?[]
                {
                    "??0WClass@Peek@@QEAA@AEBV01@@Z", 1, 0xbbccddee, "a", "b"

                }) as DllExport;
            export.TryUndecorate(module, out var undecFunc);
            TypeDump.TypeMethod? func = VftableParser.ConvertToTypeMethod(undecFunc as UndecoratedFunction);
            string childTypeLongName = "Peek::WClass";
            TypeDump typeDump = new TypeDump()
            {
                Type = childTypeLongName,
                Assembly = "libPeek_lies.dll"
            };
            RemoteRttiType childType = new RemoteRttiType(null, childTypeLongName, "libPeek_lies.dll");
            RemoteApp fakeApp = new FakeRemoteApp();

            // Act
            RttiTypesFactory.AddFunctionImpl(fakeApp, typeDump, func, childType, false);

            // Assert
            // Expecting `AddFunctionImpl` to NOT add that function (not supported yet)
            Assert.That(childType.GetMethods(), Is.Empty);
        }
    }
}
