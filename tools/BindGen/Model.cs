// One library function as BindGen sees it: facts only (name, types, registers, vector number, first version).
using System.Collections.Generic;
internal sealed record Arg(string Type, string Name, string Register);
internal sealed record Func(string Name, string Ret, IReadOnlyList<Arg> Args, int Lvo, int Since);
