# TraceDive

Exploration repo for trace analysis tools

## Hanging Task Analysis

The only currently-implemented tool is the Hanging Task Analysis tool. To use it, start collecting a trace using PerfView that includes the `System.Threading.Tasks.TplEventSource` provider (the default settings include this). Run the application that hangs. When the app hangs, wait a few seconds and stop the collection. Merge and ZIP the output (probably not needed but I'm lazy right now).

There is a sample app in `samples/SampleApp`. You can run this using `dotnet run` and press `H` to start the hanging task.

Once you have the Merged and ZIPped PerfView data, launch the `src/tracedive-cli` project and pass in the trace file path:

```
› dotnet run --project .\src\tracedive-cli\ -- C:\Users\anurse\Desktop\HangingTask.etl.zip
```

This will write a bunch of log spam and then dump a list of all processes contained in the trace including their names and command lines:

```
* 16208 dotnet "C:\Users\anurse\.dotnet\x64\dotnet.exe" complete --position 12 "dotnet .\sam"
* 5340 dotnet "C:\Users\anurse\.dotnet\x64\dotnet.exe" complete --position 18 "dotnet .\samples\S"
* 12164 dotnet "C:\Users\anurse\.dotnet\x64\dotnet.exe" complete --position 28 "dotnet .\samples\SampleApp\b"
* 16028 dotnet "C:\Users\anurse\.dotnet\x64\dotnet.exe" complete --position 32 "dotnet .\samples\SampleApp\bin\D"
* 8232 dotnet "C:\Users\anurse\.dotnet\x64\dotnet.exe" complete --position 38 "dotnet .\samples\SampleApp\bin\Debug\n"
* 25340 dotnet "C:\Users\anurse\.dotnet\x64\dotnet.exe" complete --position 53 "dotnet .\samples\SampleApp\bin\Debug\netcoreapp2.2\Sa"
* 980 dotnet "C:\Users\anurse\.dotnet\x64\dotnet.exe" .\samples\SampleApp\bin\Debug\netcoreapp2.2\SampleApp.dll
* 15636 dotnet "C:\Users\anurse\.dotnet\x64\dotnet.exe" --version
```

Re-run the command and pass in the process ID of the process to analyze as the second argument:

```
› dotnet run --project .\src\tracedive-cli\ -- C:\Users\anurse\Desktop\HangingTask.etl.zip 980
```

This will spam even more logs and then should display a list of all Task Waits that have been started but not stopped, including the full stack trace (currently there's a hacky "just my code" implementation in place):

```
Task 1 is blocking asynchronously at
    [External Code]
    sampleapp!SampleApp.HangingTaskSample+<RunAsync>d__0.MoveNext()
    [External Code]
    sampleapp!SampleApp.HangingTaskSample.RunAsync()
    sampleapp!SampleApp.Program+<Main>d__0.MoveNext()
    [External Code]
    sampleapp!SampleApp.Program.Main(class System.String[])
    sampleapp!SampleApp.Program.<Main>(class System.String[])
Task 2 is blocking asynchronously at
    [External Code]
    sampleapp!SampleApp.Program+<Main>d__0.MoveNext()
    [External Code]
    sampleapp!SampleApp.Program.Main(class System.String[])
    sampleapp!SampleApp.Program.<Main>(class System.String[])
Task 3 is blocking synchronously at
    [External Code]
    sampleapp!SampleApp.Program.<Main>(class System.String[])
```

One thing to note is that this shows all blocking tasks up the chain, there is no way in the current data to exclude. For example, in the [Sample App](samples/SampleApp) there are three waits:

* A `.Wait` in the `<Main>` method generated by the compiler to run the async Main function (Task 3)
* An `await` in the async `Main` method, waiting on `HangingTaskSample.RunAsync` (Task 2)
* An `await` in the async `HangingTaskSample.RunAsync` method, waiting on the TCS it creates, that never triggers. (Task 1)

All three of these show up in the list as "blocking" but they are all part of the same chain.