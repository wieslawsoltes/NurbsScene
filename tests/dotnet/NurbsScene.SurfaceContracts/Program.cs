using System.Text.Json;
var tests=KernelParityTests.Cases().ToArray();
var failures=0;
foreach(var(name,run)in tests)
{
    try{run();Console.WriteLine($"PASS {name}");}
    catch(Exception error){failures++;Console.Error.WriteLine($"FAIL {name}\n{error}");}
}
Console.WriteLine(JsonSerializer.Serialize(new{suite="surface-contracts",total=tests.Length,passed=tests.Length-failures,failed=failures}));
return failures==0?0:1;
