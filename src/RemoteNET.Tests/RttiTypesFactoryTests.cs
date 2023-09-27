using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NtApiDotNet.Win32;
using RemoteNET.RttiReflection;
using ScubaDiver;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver.Rtti;

namespace RemoteNET.Tests
{
    [TestFixture]

    public class RttiTypesFactoryTests
    {
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
            RemoteRttiType childType = new RemoteRttiType(null, childTypeLongName, "libPeek_lies.dll");

            // Act
            RttiTypesFactory.AddFunctionImpl(func, childType, false);

            // Assert
            MethodInfo method = childType.GetMethods().Single();
            string decTypeLongName = $"{method.DeclaringType.Namespace}::{method.DeclaringType.Name}";
            Assert.That(childTypeLongName, Is.Not.EqualTo(decTypeLongName));
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
            RemoteRttiType childType = new RemoteRttiType(null, childTypeLongName, "libPeek_lies.dll");

            // Act
            RttiTypesFactory.AddFunctionImpl(func, childType, false);

            // Assert
            MethodInfo method = childType.GetMethods().Single();
            string decTypeLongName = $"{method.DeclaringType.Namespace}::{method.DeclaringType.Name}";
            Assert.That(childTypeLongName, Is.EqualTo(decTypeLongName));
            Assert.That(childType, Is.EqualTo(method.DeclaringType));
        }
    }
}
