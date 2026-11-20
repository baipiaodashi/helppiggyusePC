namespace PiggyHelper.Services;

/// <summary>一家 AI 服务（都兼容 OpenAI 的接口格式）。</summary>
public sealed record AiProvider(string Id, string Name, string Url, string Model, string KeyUrl, string Intro);

public class AiException : Exception
{
    public AiException(string message) : base(message) { }
}

/// <summary>这个 AI 平台或模型不支持"工具调用"（也就是不能让小猪动手）。</summary>
public sealed class ToolsNotSupportedException : AiException
{
    public ToolsNotSupportedException() : base("这个 AI 模型不支持让小猪动手。") { }
}
