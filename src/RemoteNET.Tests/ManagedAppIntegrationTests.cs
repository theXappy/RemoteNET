using System.Text;
using TestTarget;

#pragma warning disable CS8602 // Dereference of a possibly null reference.
namespace RemoteNET.Tests
{

    [TestFixture]
    internal class ManagedAppIntegrationTests
    {
        private Random r = new Random();
        private string TestTargetExe => Path.ChangeExtension(typeof(TestClass).Assembly.Location, "exe");


        [Test]
        public void ConnectRemoteApp()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);

            // Act
            var app = RemoteAppFactory.Connect(target.Process, RuntimeType.Managed);

            // Assert
            Assert.That(app, Is.Not.Null);
            app.Dispose();
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void CheckAlivenessRemoteApp(int sleepSeconds)
        {
            if (sleepSeconds > 1)
                Thread.Sleep(10);
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);

            // Act
            var app = RemoteAppFactory.Connect(target.Process, RuntimeType.Managed);
            Thread.Sleep(TimeSpan.FromSeconds(sleepSeconds));
            bool alive = app.Communicator.CheckAliveness();

            // Assert
            Assert.That(alive, Is.True);
            var unmanAppp = RemoteAppFactory.Connect(target.Process, RuntimeType.Unmanaged);
            app.Dispose();
            unmanAppp.Dispose();
        }

        [Test]
        public void FindObjectInHeap()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);

            // Act
            var app = RemoteAppFactory.Connect(target.Process, RuntimeType.Managed);
            List<CandidateObject>? candidates = null;
            candidates = app.QueryInstances(typeof(TestClass)).ToList();
            var ro = app.GetRemoteObject(candidates.Single());

            // Assert
            Assert.That(ro, Is.Not.Null);
            Assert.That(ro.GetType().Name, Is.EqualTo(typeof(TestClass).Name));
        }

        private RemoteObject GetSingleTestObject(DisposableTarget target, out ManagedRemoteApp app)
        {
            app = (ManagedRemoteApp)RemoteAppFactory.Connect(target.Process, RuntimeType.Managed);
            List<CandidateObject>? candidates = null;
            candidates = app.QueryInstances(typeof(TestClass)).ToList();
            var ro = app.GetRemoteObject(candidates.Single());
            return ro;
        }

        [Test]
        public void ReadRemoteField()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);
            RemoteObject ro = GetSingleTestObject(target, out _);
            dynamic dro = ro.Dynamify();

            // Act
            object res = dro.TestField1;

            // Assert
            Assert.That(res, Is.Not.Null);
            Assert.That(res, Is.TypeOf<int>());
            Assert.That(res, Is.EqualTo(5));
        }

        [Test]
        public void ReadRemoteReadOnlyProperty()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);
            RemoteObject ro = GetSingleTestObject(target, out _);
            dynamic dro = ro.Dynamify();

            // Act
            object res = dro.TestProp1;

            // Assert
            Assert.That(res, Is.Not.Null);
            Assert.That(res, Is.TypeOf<int>());
            Assert.That(res, Is.EqualTo(6));
        }

        [Test]
        public void ReadWriteRemoteModifiableProperty()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);
            RemoteObject ro = GetSingleTestObject(target, out _);
            dynamic dro = ro.Dynamify();
            int newWriteValue = 1337;

            // Act
            object originalReadValue = dro.TestProp2;
            dro.TestProp2 = newWriteValue;
            object newReadValue = dro.TestProp2;

            // Assert
            Assert.That(originalReadValue, Is.Not.Null);
            Assert.That(originalReadValue, Is.TypeOf<int>());
            Assert.That(originalReadValue, Is.EqualTo(7));
            Assert.That(newReadValue, Is.Not.Null);
            Assert.That(newReadValue, Is.TypeOf<int>());
            Assert.That(newReadValue, Is.EqualTo(newWriteValue));
        }

        [Test]
        public void InvokeRemoteParameterlessMethod()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);
            RemoteObject ro = GetSingleTestObject(target, out _);
            dynamic dro = ro.Dynamify();

            // Act
            object res = dro.TestMethod1();

            // Assert
            Assert.That(res, Is.Not.Null);
            Assert.That(res, Is.TypeOf<int>());
            Assert.That(res, Is.EqualTo(8));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void InvokeRemoteParameteredMethod(int input)
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);
            RemoteObject ro = GetSingleTestObject(target, out _);
            dynamic dro = ro.Dynamify();

            // Act
            // TestMethod2 just does `input + 9`
            object res = dro.TestMethod2(input);

            // Assert
            Assert.That(res, Is.Not.Null);
            Assert.That(res, Is.TypeOf<int>());
            Assert.That(res, Is.EqualTo(input + 9));
        }

        [Test]
        public void InvokeRemoteParameterlessCtor()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);
            var app = RemoteAppFactory.Connect(target.Process, RuntimeType.Managed) as ManagedRemoteApp;

            // Act
            ManagedRemoteObject res = app.Activator.CreateInstance(typeof(StringBuilder)) as ManagedRemoteObject;

            // Assert
            Assert.That(res, Is.Not.Null);
            Assert.That(res.GetRemoteType().FullName, Is.EqualTo(typeof(StringBuilder).FullName));
        }

        [Test]
        public void InvokeRemoteMethodWithRemoteParameter()
        {
            // Arrange
            using var target = new DisposableTarget(TestTargetExe);
            var ro = GetSingleTestObject(target, out ManagedRemoteApp app);
            dynamic dro = ro.Dynamify();

            // Act
            ManagedRemoteObject stringBuilderParameter = app.Activator.CreateInstance(typeof(StringBuilder)) as ManagedRemoteObject;
            dro.TestMethod3(stringBuilderParameter);
            string res = stringBuilderParameter.Dynamify().ToString();

            // Assert
            Assert.That(res, Is.Not.Null);
            Assert.That(res, Is.EqualTo("10"));
        }
    }
}
#pragma warning restore CS8602 // Dereference of a possibly null reference.
