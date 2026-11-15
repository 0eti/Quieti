namespace EarTrumpet.Quieti;

/// <summary>프로세스 하나: pid와 앱 이름(AppRules.AppKey 결과, 못 읽으면 "").</summary>
public readonly record struct Proc(uint Pid, string Exe);

/// <summary>앞 창: 그 창 프로세스의 앱 이름, 창 클래스, 이 프로그램의 창인가.</summary>
public sealed record FgWindow(string Exe, string Class, bool Own);
