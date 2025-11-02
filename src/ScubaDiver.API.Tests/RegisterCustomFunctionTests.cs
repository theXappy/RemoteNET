using ScubaDiver.API.Interactions;
using System.Collections.Generic;

namespace ScubaDiver.API.Tests
{
    [TestFixture]
    public class RegisterCustomFunctionTests
    {
        [Test]
        public void RegisterCustomFunctionRequest_ValidData_SetsPropertiesCorrectly()
        {
            // Arrange
            var request = new RegisterCustomFunctionRequest
            {
                ParentTypeFullName = "MyNamespace::MyClass",
                ParentAssembly = "MyModule.dll",
                FunctionName = "MyCustomFunction",
                ModuleName = "MyModule.dll",
                Offset = 0x1234,
                ReturnTypeFullName = "int",
                ReturnTypeAssembly = "System.Private.CoreLib",
                Parameters = new List<RegisterCustomFunctionRequest.ParameterTypeInfo>
                {
                    new RegisterCustomFunctionRequest.ParameterTypeInfo
                    {
                        Name = "param1",
                        TypeFullName = "int",
                        Assembly = "System.Private.CoreLib"
                    },
                    new RegisterCustomFunctionRequest.ParameterTypeInfo
                    {
                        Name = "param2",
                        TypeFullName = "float",
                        Assembly = "System.Private.CoreLib"
                    }
                }
            };

            // Assert
            Assert.That(request.ParentTypeFullName, Is.EqualTo("MyNamespace::MyClass"));
            Assert.That(request.ParentAssembly, Is.EqualTo("MyModule.dll"));
            Assert.That(request.FunctionName, Is.EqualTo("MyCustomFunction"));
            Assert.That(request.ModuleName, Is.EqualTo("MyModule.dll"));
            Assert.That(request.Offset, Is.EqualTo(0x1234));
            Assert.That(request.ReturnTypeFullName, Is.EqualTo("int"));
            Assert.That(request.Parameters.Count, Is.EqualTo(2));
            Assert.That(request.Parameters[0].Name, Is.EqualTo("param1"));
            Assert.That(request.Parameters[0].TypeFullName, Is.EqualTo("int"));
            Assert.That(request.Parameters[1].Name, Is.EqualTo("param2"));
            Assert.That(request.Parameters[1].TypeFullName, Is.EqualTo("float"));
        }

        [Test]
        public void RegisterCustomFunctionResponse_Success_SetsPropertiesCorrectly()
        {
            // Arrange
            var response = new RegisterCustomFunctionResponse
            {
                Success = true,
                ErrorMessage = null
            };

            // Assert
            Assert.That(response.Success, Is.True);
            Assert.That(response.ErrorMessage, Is.Null);
        }

        [Test]
        public void RegisterCustomFunctionResponse_Failure_SetsPropertiesCorrectly()
        {
            // Arrange
            var response = new RegisterCustomFunctionResponse
            {
                Success = false,
                ErrorMessage = "Failed to register custom function"
            };

            // Assert
            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Is.EqualTo("Failed to register custom function"));
        }
    }
}
