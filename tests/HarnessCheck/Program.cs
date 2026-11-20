// 用法：HarnessCheck <报告文件> [假 AI 接口地址]
var report = args.Length > 0 ? args[0] : "harness-selftest.txt";
var url = args.Length > 1 ? args[1] : "http://127.0.0.1:18080/v1";
return await PiggyHelper.Services.Harness.HarnessSelfTest.RunAsync(report, url);
