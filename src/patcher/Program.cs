using Mono.Cecil;
// usage: Patcher dump <dll> <typeFullNameSubstring> | Patcher fixsteam <in> <out>
var mode = args[0];
var m = ModuleDefinition.ReadModule(args[1]);
if (mode == "dump") {
    foreach (var t in m.GetTypes().Where(t => t.FullName.Contains(args[2]))) {
        Console.WriteLine($"{t.FullName} layout={(t.IsExplicitLayout ? "explicit" : t.IsSequentialLayout ? "seq" : "auto")}");
        foreach (var f in t.Fields) Console.WriteLine($"   {f.FieldType.FullName} {f.Name} off={f.Offset} static={f.IsStatic}");
    }
}
if (mode == "fixsteam") {
    // explicit-layout structs whose object-reference fields overlap other fields are rejected by NativeAOT: drop those fields
    foreach (var t in m.GetTypes().Where(t => t.IsExplicitLayout && t.IsValueType)) {
        var bad = t.Fields.Where(f => !f.IsStatic && !f.FieldType.IsValueType && !f.FieldType.IsPointer).ToList();
        if (bad.Count == 0 || t.Fields.Count(f => !f.IsStatic) < 2) continue;
        foreach (var f in bad) { Console.WriteLine($"removing {t.FullName}.{f.Name} ({f.FieldType.Name})"); t.Fields.Remove(f); }
    }
    m.Write(args[2]);
}
if (mode == "redirect") {
    // Patcher redirect <game.dll> <patches.dll> <out.dll>
    var res = new DefaultAssemblyResolver(); res.AddSearchDirectory(Path.GetDirectoryName(args[1])); res.AddSearchDirectory(Path.GetDirectoryName(args[2])); res.AddSearchDirectory("/game");
    var game = ModuleDefinition.ReadModule(args[1], new ReaderParameters { AssemblyResolver = res, ReadWrite = false });
    var patches = ModuleDefinition.ReadModule(args[2], new ReaderParameters { AssemblyResolver = res });
    int n = 0;
    foreach (var pt in patches.GetTypes())
    foreach (var pm in pt.Methods.Where(x => x.IsStatic))
    foreach (var ca in pm.CustomAttributes.Where(c => c.AttributeType.Name == "ReplaceAttribute")) {
        var tn = (string)ca.ConstructorArguments[0].Value; var mn = (string)ca.ConstructorArguments[1].Value;
        var tt = game.GetType(tn) ?? game.GetTypes().FirstOrDefault(t => t.FullName == tn.Replace('+', '/'));
        if (tt == null) throw new Exception("type not found: " + tn);
        var cands = tt.Methods.Where(x => x.Name == mn && x.HasBody && x.Parameters.Count + (x.IsStatic ? 0 : 1) == pm.Parameters.Count).ToList();
        if (cands.Count != 1) throw new Exception($"{tn}.{mn}: {cands.Count} candidates with {pm.Parameters.Count} params");
        var target = cands[0];
        var body = target.Body; body.Instructions.Clear(); body.ExceptionHandlers.Clear(); body.Variables.Clear(); body.InitLocals = false;
        var il = body.GetILProcessor();
        for (int i = 0; i < pm.Parameters.Count; i++) il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldarg, i < (target.IsStatic ? target.Parameters.Count : target.Parameters.Count + 1) ? (target.IsStatic ? target.Parameters[i] : (i == 0 ? body.ThisParameter : target.Parameters[i - 1])) : null));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Call, game.ImportReference(pm)));
        il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
        Console.WriteLine($"redirected {tn}.{mn} -> {pt.FullName}.{pm.Name}"); n++;
    }
    // [Prefix(type, method)]: a static bool method runs first; returning true skips the original body (void targets only)
    foreach (var pt in patches.GetTypes())
    foreach (var pm in pt.Methods.Where(x => x.IsStatic))
    foreach (var ca in pm.CustomAttributes.Where(c => c.AttributeType.Name == "PrefixAttribute")) {
        var tn = (string)ca.ConstructorArguments[0].Value; var mn = (string)ca.ConstructorArguments[1].Value;
        var tt = game.GetType(tn) ?? throw new Exception("type not found: " + tn);
        // void target: static bool P(self, args...); non-void target: static bool P(self, args..., ref TResult result),
        // and returning true returns result instead of running the original
        bool hasResult = pm.Parameters.Count > 0 && pm.Parameters[^1].ParameterType.IsByReference;
        int argCount = pm.Parameters.Count - (hasResult ? 1 : 0);
        var cands = tt.Methods.Where(x => x.Name == mn && x.HasBody && x.Parameters.Count + (x.IsStatic ? 0 : 1) == argCount).ToList();
        if (cands.Count != 1) throw new Exception($"{tn}.{mn}: {cands.Count} candidates with {argCount} params");
        var target = cands[0];
        if (pm.ReturnType.MetadataType != MetadataType.Boolean || (target.ReturnType.MetadataType == MetadataType.Void) == hasResult)
            throw new Exception($"prefix {tn}.{mn}: bool prefix required, with a trailing ref result parameter exactly when the target returns a value");
        var il = target.Body.GetILProcessor(); var first = target.Body.Instructions[0];
        Mono.Cecil.Cil.VariableDefinition result = null;
        if (hasResult) { result = new Mono.Cecil.Cil.VariableDefinition(target.ReturnType); target.Body.Variables.Add(result); target.Body.InitLocals = true; }
        for (int i = 0; i < argCount; i++)
            il.InsertBefore(first, il.Create(Mono.Cecil.Cil.OpCodes.Ldarg, target.IsStatic ? target.Parameters[i] : (i == 0 ? target.Body.ThisParameter : target.Parameters[i - 1])));
        if (hasResult) il.InsertBefore(first, il.Create(Mono.Cecil.Cil.OpCodes.Ldloca, result));
        il.InsertBefore(first, il.Create(Mono.Cecil.Cil.OpCodes.Call, game.ImportReference(pm)));
        il.InsertBefore(first, il.Create(Mono.Cecil.Cil.OpCodes.Brfalse, first));
        if (hasResult) il.InsertBefore(first, il.Create(Mono.Cecil.Cil.OpCodes.Ldloc, result));
        il.InsertBefore(first, il.Create(Mono.Cecil.Cil.OpCodes.Ret));
        Console.WriteLine($"prefixed {tn}.{mn} with {pt.FullName}.{pm.Name}"); n++;
    }
    // [ViewportFix(type, method)]: GraphicsDevice.get_Viewport calls in that method -> the marked helper, except
    // zoom-aware ones (a zoomLevel/uiScale read within the next 8 instructions, before any other viewport read)
    foreach (var pt in patches.GetTypes())
    foreach (var pm in pt.Methods.Where(x => x.IsStatic))
    foreach (var ca in pm.CustomAttributes.Where(c => c.AttributeType.Name == "ViewportFixAttribute")) {
        var tn = (string)ca.ConstructorArguments[0].Value; var mn = (string)ca.ConstructorArguments[1].Value;
        var tt = game.GetType(tn) ?? throw new Exception("type not found: " + tn);
        var helper = game.ImportReference(pm); int fixedSites = 0, kept = 0;
        foreach (var target in tt.Methods.Where(x => x.Name == mn && x.HasBody)) {
            var ins = target.Body.Instructions;
            for (int i = 0; i < ins.Count; i++) {
                if (!(ins[i].Operand is MethodReference mr) || mr.Name != "get_Viewport" || mr.DeclaringType.Name != "GraphicsDevice") continue;
                bool zoomAware = false;
                for (int j = i + 1; j < Math.Min(ins.Count, i + 9); j++) {
                    if (!(ins[j].Operand is MethodReference z)) continue;
                    if (z.Name == "get_Viewport") break;   // the next read decides for itself
                    if (z.Name == "get_zoomLevel" || z.Name == "get_uiScale") { zoomAware = true; break; }
                }
                if (zoomAware) { kept++; Console.WriteLine($"  viewport kept (zoom-aware) {tn}.{mn} IL_{ins[i].Offset:x4}"); continue; }
                ins[i].OpCode = Mono.Cecil.Cil.OpCodes.Call; ins[i].Operand = helper; fixedSites++;
            }
        }
        if (fixedSites == 0) throw new Exception($"viewport fix {tn}.{mn}: no get_Viewport call found");
        Console.WriteLine($"viewport fixed {tn}.{mn}: {fixedSites} sites ({kept} zoom-aware kept)"); n++;
    }
    game.Write(args[3]);
    Console.WriteLine($"{n} methods redirected");
}
if (mode == "calls") {
    // Patcher calls <dll> <methodNameOfCallee>: list methods that call a member with that name, with the call's operand
    foreach (var t in m.GetTypes()) foreach (var me in t.Methods.Where(x => x.HasBody))
        foreach (var ins in me.Body.Instructions)
            if (ins.Operand is MethodReference mr && mr.Name == args[2]) Console.WriteLine($"{t.FullName}::{me.Name}  ->  {mr.FullName}  (prev: {ins.Previous?.OpCode} {ins.Previous?.Operand})");
}
if (mode == "xmlfix") {
    // Patcher xmlfix <System.Private.Xml.dll> <patches.dll> <out>
    var res = new DefaultAssemblyResolver(); res.AddSearchDirectory(Path.GetDirectoryName(args[1])); res.AddSearchDirectory(Path.GetDirectoryName(args[2]));
    var xm = ModuleDefinition.ReadModule(args[1], new ReaderParameters { AssemblyResolver = res });
    var pm = ModuleDefinition.ReadModule(args[2], new ReaderParameters { AssemblyResolver = res });
    var helper = pm.GetType("SdvAot.XmlFix").Methods.First(x => x.Name == "GetMethod");
    var imported = xm.ImportReference(helper);
    int n = 0;
    foreach (var t in xm.GetTypes().Where(t => t.FullName.StartsWith("System.Xml.Serialization.ReflectionXmlSerializationReader")))
    foreach (var me in t.Methods.Where(x => x.HasBody))
    foreach (var ins in me.Body.Instructions)
        if (ins.Operand is MethodReference mr && mr.Name == "GetMethod" && mr.Parameters.Count == 1 && mr.DeclaringType.FullName == "System.Type") {
            ins.OpCode = Mono.Cecil.Cil.OpCodes.Call; ins.Operand = imported; n++;
            Console.WriteLine($"xmlfix: {t.Name}::{me.Name}");
        }
    var create = xm.ImportReference(pm.GetType("SdvAot.XmlFix").Methods.First(x => x.Name == "CreateCollection"));
    foreach (var me in xm.GetType("System.Xml.Serialization.ReflectionXmlSerializationReader").Methods.Where(x => x.Name == "SetCollectionObjectWithCollectionMember"))
    foreach (var ins in me.Body.Instructions)
        if (ins.Operand is MethodReference cr && cr.Name == "ReflectionCreateObject") { ins.Operand = create; n++; Console.WriteLine("xmlfix: SetCollectionObjectWithCollectionMember temp collection"); }
    var wrap = xm.ImportReference(pm.GetType("SdvAot.XmlFix").Methods.First(x => x.Name == "WrapField"));
    var wrapP = xm.ImportReference(pm.GetType("SdvAot.XmlFix").Methods.First(x => x.Name == "WrapProperty"));
    foreach (var me in xm.GetType("System.Xml.Serialization.ReflectionXmlSerializationReader").Methods.Where(x => x.Name == "GetSetMemberValueDelegate")) {
        var list = me.Body.Instructions.ToList();
        foreach (var ins in list)
            if ((ins.OpCode == Mono.Cecil.Cil.OpCodes.Ldftn || ins.OpCode == Mono.Cecil.Cil.OpCodes.Ldvirtftn) && ins.Operand is MethodReference sr && sr.Name == "SetValue" && sr.DeclaringType.Name == "FieldInfo") {
                // ldloc fieldInfo; ldftn FieldInfo::SetValue; newobj delegate  ->  ldloc fieldInfo; call WrapField; ldftn ...
                me.Body.GetILProcessor().InsertBefore(ins, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Call, wrap));
                n++; Console.WriteLine("xmlfix: GetSetMemberValueDelegate field setter wrapped");
            }
            else if ((ins.OpCode == Mono.Cecil.Cil.OpCodes.Ldftn || ins.OpCode == Mono.Cecil.Cil.OpCodes.Ldvirtftn) && ins.Operand is MethodReference sp && sp.Name == "SetValue" && sp.DeclaringType.Name == "PropertyInfo") {
                me.Body.GetILProcessor().InsertBefore(ins, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Call, wrapP));
                n++; Console.WriteLine("xmlfix: GetSetMemberValueDelegate property setter wrapped");
            }
    }
    var should = xm.ImportReference(pm.GetType("SdvAot.XmlFix").Methods.First(x => x.Name == "ShouldUseReflection"));
    foreach (var me in xm.GetType("System.Xml.Serialization.XmlSerializer").Methods.Where(x => x.HasBody))
    foreach (var ins in me.Body.Instructions)
        if (ins.Operand is MethodReference sr2 && sr2.Name == "ShouldUseReflectionBasedSerialization" && ins.Previous?.OpCode == Mono.Cecil.Cil.OpCodes.Ldfld
            && ins.Previous.Operand is FieldReference fr && fr.Name == "_mapping") {
            ins.Previous.OpCode = Mono.Cecil.Cil.OpCodes.Nop; ins.Previous.Operand = null;   // ldarg.0; ldfld _mapping; call S(mapping) -> ldarg.0; nop; call Helper(this)
            ins.Operand = should; n++;
            Console.WriteLine($"xmlfix: XmlSerializer::{me.Name} reflection check");
        }
    xm.Attributes |= ModuleAttributes.ILOnly;   // ReadyToRun image: keep the IL only (NativeAOT compiles from IL)
    xm.Write(args[3]);
    Console.WriteLine($"{n} GetMethod(string) calls redirected");
}
if (mode == "il") {
    foreach (var t in m.GetTypes().Where(t => t.FullName == args[2])) foreach (var me in t.Methods.Where(x => x.Name == args[3] && x.HasBody))
        foreach (var ins in me.Body.Instructions) Console.WriteLine(ins);
}
if (mode == "types") {
    foreach (var t in m.GetTypes().Where(t => !t.Name.StartsWith("<")).OrderBy(t => t.FullName)) Console.WriteLine($"{(t.IsPublic || t.IsNestedPublic ? "pub" : "int")} {t.FullName} methods={t.Methods.Count}");
}
if (mode == "refs") {
    // Patcher refs <dll> <assemblyName>: member references into that assembly
    foreach (var r in m.GetMemberReferences().Where(r => (r.DeclaringType.Scope?.Name ?? "") == args[2]).Select(r => r.FullName).Distinct().OrderBy(x => x)) Console.WriteLine(r);
    foreach (var r in m.GetTypeReferences().Where(r => (r.Scope?.Name ?? "") == args[2]).Select(r => "TYPE " + r.FullName).Distinct().OrderBy(x => x)) Console.WriteLine(r);
}
if (mode == "xmlnew") {
    // Patcher xmlnew <game.dll> <SdvXmlSerializers.dll> <out.dll>: every "new XmlSerializer(Type[, Type[]])" -> SdvAot.Xml.Factory.Create
    var res = new DefaultAssemblyResolver(); foreach (var d in new[] { Path.GetDirectoryName(args[1]), Path.GetDirectoryName(args[2]), "/game" }) res.AddSearchDirectory(d);
    var game = ModuleDefinition.ReadModule(args[1], new ReaderParameters { AssemblyResolver = res, InMemory = true });
    var xs = ModuleDefinition.ReadModule(args[2], new ReaderParameters { AssemblyResolver = res });
    var factory = xs.GetType("SdvAot.Xml.Factory");
    var create1 = game.ImportReference(factory.Methods.First(x => x.Name == "Create" && x.Parameters.Count == 1));
    var create2 = game.ImportReference(factory.Methods.First(x => x.Name == "Create" && x.Parameters.Count == 2));
    int n = 0;
    foreach (var t in game.GetTypes()) foreach (var me in t.Methods.Where(x => x.HasBody))
        foreach (var ins in me.Body.Instructions)
            if (ins.OpCode == Mono.Cecil.Cil.OpCodes.Newobj && ins.Operand is MethodReference ctor && ctor.DeclaringType.FullName == "System.Xml.Serialization.XmlSerializer") {
                var ps = ctor.Parameters.Select(p => p.ParameterType.FullName).ToArray();
                if (ps.SequenceEqual(new[] { "System.Type" })) ins.Operand = create1;
                else if (ps.SequenceEqual(new[] { "System.Type", "System.Type[]" })) ins.Operand = create2;
                else { Console.WriteLine($"xmlnew: unhandled ctor {ctor.FullName} in {t.FullName}::{me.Name}"); continue; }
                ins.OpCode = Mono.Cecil.Cil.OpCodes.Call; n++;
                Console.WriteLine($"xmlnew: {t.FullName}::{me.Name}");
            }
    game.Write(args[3]);
    Console.WriteLine($"{n} XmlSerializer constructions redirected");
}
if (mode == "netslim") {
    // Patcher netslim <game.dll> <patches.dll> <out.dll>
    var res = new DefaultAssemblyResolver(); foreach (var d in new[] { Path.GetDirectoryName(args[1]), Path.GetDirectoryName(args[2]), "/game" }) res.AddSearchDirectory(d);
    var game = ModuleDefinition.ReadModule(args[1], new ReaderParameters { AssemblyResolver = res, InMemory = true });
    var pm = ModuleDefinition.ReadModule(args[2], new ReaderParameters { AssemblyResolver = res });
    var noName = game.ImportReference(pm.GetType("SdvAot.NetSlim").Methods.First(x => x.Name == "NoName"));
    int n = 0;
    var nf = game.GetType("Netcode.NetFields");
    foreach (var ins in nf.Methods.First(x => x.Name == "AddField").Body.Instructions)
        if (ins.Operand is MethodReference c && c.Name == "Concat" && c.Parameters.Count == 3 && ins.Next?.Operand is MethodReference sn && sn.Name == "set_Name") {
            ins.Operand = noName; n++; Console.WriteLine("netslim: AddField name");
        }
    var gni = nf.Methods.First(x => x.Name == "GetNameForInstance");
    gni.Body.Instructions.Clear(); gni.Body.Variables.Clear(); gni.Body.ExceptionHandlers.Clear();
    gni.Body.GetILProcessor().Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldnull));
    gni.Body.GetILProcessor().Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret)); n++; Console.WriteLine("netslim: GetNameForInstance -> null");
    var ans = game.GetType("Netcode.AbstractNetSerializable");
    var sds = ans.Methods.First(x => x.Name == "SetDirtySooner");
    var il = sds.Body.GetILProcessor();
    foreach (var ins in sds.Body.Instructions.ToList())
        if (ins.Operand is MethodReference set && set.Name == "Set" && set.DeclaringType.Name == "NetVersion") {
            // stack: &ChangeVersion, NetVersion -> drop both
            ins.OpCode = Mono.Cecil.Cil.OpCodes.Pop; ins.Operand = null;
            il.InsertAfter(ins, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Pop)); n++; Console.WriteLine("netslim: SetDirtySooner ChangeVersion.Set dropped");
        }
    // Part 2: drop per-object fields single player never needs (rewrite every access in the module, then delete them).
    var nfb = game.GetType("Netcode.NetFieldBase`2");
    var drop = new Dictionary<FieldDefinition, string> {
        [ans.Fields.First(f => f.Name == "<Name>k__BackingField")] = "null",
        [ans.Fields.First(f => f.Name == "minNextDirtyTime")] = "zero",
        [nfb.Fields.First(f => f.Name == "interpolationStartTick")] = "zero",
        [nfb.Fields.First(f => f.Name == "previousValue")] = "value",
        [nfb.Fields.First(f => f.Name == "targetValue")] = "value",
    };
    var valueDef = nfb.Fields.First(f => f.Name == "value");
    int rewrites = 0;
    foreach (var t in game.GetTypes()) foreach (var me in t.Methods.Where(x => x.HasBody)) {
        var proc = me.Body.GetILProcessor();
        foreach (var ins in me.Body.Instructions.ToList()) {
            if (ins.Operand is not FieldReference fr) continue;
            FieldDefinition fd; try { fd = fr.Resolve(); } catch { continue; }
            if (fd == null || !drop.TryGetValue(fd, out var how)) continue;
            var op = ins.OpCode;
            if (op == Mono.Cecil.Cil.OpCodes.Stfld) {                       // obj, val -> (nothing)
                ins.OpCode = Mono.Cecil.Cil.OpCodes.Pop; ins.Operand = null;
                proc.InsertAfter(ins, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Pop));
            } else if (op == Mono.Cecil.Cil.OpCodes.Ldflda && ins.Next?.OpCode == Mono.Cecil.Cil.OpCodes.Initobj) {
                // "field = default(T)" for generic T: ldflda field; initobj T -> drop the object, nothing to clear
                ins.OpCode = Mono.Cecil.Cil.OpCodes.Pop; ins.Operand = null;
                ins.Next.OpCode = Mono.Cecil.Cil.OpCodes.Nop; ins.Next.Operand = null;
            } else if (op == Mono.Cecil.Cil.OpCodes.Ldfld || op == Mono.Cecil.Cil.OpCodes.Ldflda) {
                if (how == "value") {
                    // same declaring (generic instance) type, field "value"
                    var vr = new FieldReference("value", fr.FieldType, fr.DeclaringType);
                    vr.FieldType = valueDef.FieldType;
                    ins.Operand = game.ImportReference(vr);
                } else if (op == Mono.Cecil.Cil.OpCodes.Ldfld) {              // obj -> null / 0
                    ins.OpCode = Mono.Cecil.Cil.OpCodes.Pop; ins.Operand = null;
                    proc.InsertAfter(ins, Mono.Cecil.Cil.Instruction.Create(how == "null" ? Mono.Cecil.Cil.OpCodes.Ldnull : Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
                } else throw new Exception($"address taken of dropped field {fd.Name} in {t.FullName}::{me.Name}");
            } else throw new Exception($"unexpected {op} on {fd.Name} in {t.FullName}::{me.Name}");
            rewrites++;
        }
    }
    foreach (var fd in drop.Keys) fd.DeclaringType.Fields.Remove(fd);
    Console.WriteLine($"netslim: {rewrites} accesses rewritten, {drop.Count} fields removed");
    n++;
    game.Write(args[3]);
    Console.WriteLine($"{n} netslim edits");
}
if (mode == "xtile") {
    // Patcher xtile <xTile.dll> <out.dll>: Component creates its PropertyCollection on first access instead of in the
    // constructor (every map tile carried an empty one: 248k collections, ~16 MB on PC)
    var xt = ModuleDefinition.ReadModule(args[1], new ReaderParameters { InMemory = true });
    var comp = xt.GetType("xTile.ObjectModel.Component");
    var field = comp.Fields.First(f => f.Name == "m_propertyCollection");
    var pcCtor = xt.GetType("xTile.ObjectModel.PropertyCollection").Methods.First(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
    int n = 0;
    foreach (var ctor in comp.Methods.Where(m => m.IsConstructor && !m.IsStatic)) {
        var ins = ctor.Body.Instructions;
        for (int i = 0; i + 2 < ins.Count; i++)
            if (ins[i].OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_0 && ins[i + 1].OpCode == Mono.Cecil.Cil.OpCodes.Newobj && ins[i + 2].OpCode == Mono.Cecil.Cil.OpCodes.Stfld && ((FieldReference)ins[i + 2].Operand).Name == field.Name) {
                for (int k = 0; k < 3; k++) { ins[i + k].OpCode = Mono.Cecil.Cil.OpCodes.Nop; ins[i + k].Operand = null; }
                n++; Console.WriteLine($"xtile: {ctor.FullName}: no eager PropertyCollection");
            }
    }
    var getter = comp.Methods.First(m => m.Name == "get_Properties");
    var b = getter.Body; b.Instructions.Clear(); b.Variables.Clear();
    var il = b.GetILProcessor();
    var load = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0);
    il.Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
    il.Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldfld, field));
    il.Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Brtrue_S, load));
    il.Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
    il.Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Newobj, pcCtor));
    il.Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Stfld, field));
    il.Append(load);
    il.Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldfld, field));
    il.Append(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
    n++; Console.WriteLine("xtile: get_Properties creates on first access");
    xt.Write(args[2]);
    Console.WriteLine($"{n} xtile edits");
}
if (mode == "consts") {
    foreach (var t in m.GetTypes()) foreach (var f in t.Fields.Where(f => f.HasConstant && f.Name.Contains(args[2]))) Console.WriteLine($"{t.Name}.{f.Name} = {f.Constant}");
}
if (mode == "rt16") {
    // Patcher rt16 <game.dll> <out.dll>: Game1.SetWindowSize's full-screen render targets in 16-bit:
    // "Screen" (world, opaque) -> Bgr565, "UI Screen" (composited with alpha) -> Bgra4444. 1280x960 each at 0.5 zoom/UI scale.
    var game = ModuleDefinition.ReadModule(args[1], new ReaderParameters { InMemory = true, AssemblyResolver = new DefaultAssemblyResolver() });
    ((DefaultAssemblyResolver)game.AssemblyResolver).AddSearchDirectory(Path.GetDirectoryName(args[1])); ((DefaultAssemblyResolver)game.AssemblyResolver).AddSearchDirectory("/game");
    var sws = game.GetType("StardewValley.Game1").Methods.First(x => x.Name == "SetWindowSize");
    var news = sws.Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Newobj && i.Operand is MethodReference c && c.DeclaringType.Name == "RenderTarget2D" && c.Parameters.Count == 8).ToList();
    if (news.Count != 2) throw new Exception($"expected 2 RenderTarget2D in SetWindowSize, found {news.Count}");
    int[] formats = { 1, 3 };   // SurfaceFormat.Bgr565, SurfaceFormat.Bgra4444
    for (int k = 0; k < 2; k++) {
        var f = news[k].Previous.Previous.Previous.Previous;   // device, w, h, mip, FORMAT, depth, msaa, usage
        if (f.OpCode != Mono.Cecil.Cil.OpCodes.Ldc_I4_0) throw new Exception($"unexpected format operand {f}");
        f.OpCode = formats[k] == 1 ? Mono.Cecil.Cil.OpCodes.Ldc_I4_1 : Mono.Cecil.Cil.OpCodes.Ldc_I4_3;
        Console.WriteLine($"rt16: render target {k} -> SurfaceFormat {formats[k]}");
    }
    game.Write(args[2]);
}
if (mode == "loadclear") {
    // Patcher loadclear <game.dll> <out.dll>: SaveGame.loadDataToLocations(fromLocations) ends with
    // fromLocations.Clear(); GC.Collect(): the deserialized locations (a second copy of the world) are not used again,
    // and the rest of the load (lights, schedules) otherwise runs with both copies alive.
    var resLc = new DefaultAssemblyResolver(); resLc.AddSearchDirectory(Path.GetDirectoryName(args[1])); resLc.AddSearchDirectory("/game");
    var game = ModuleDefinition.ReadModule(args[1], new ReaderParameters { InMemory = true, AssemblyResolver = resLc });
    var m2 = game.GetType("StardewValley.SaveGame").Methods.First(x => x.Name == "loadDataToLocations");
    var listType = m2.Parameters[0].ParameterType;   // List<GameLocation>
    var clearDef = game.ImportReference(typeof(List<>)).Resolve().Methods.First(x => x.Name == "Clear");
    var clear = new MethodReference("Clear", game.TypeSystem.Void, listType) { HasThis = true };
    var collect = game.ImportReference(typeof(GC).GetMethod("Collect", Type.EmptyTypes));
    var il = m2.Body.GetILProcessor();
    int n2 = 0;
    foreach (var ret in m2.Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret).ToList()) {
        var first = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        // branches that jumped to "ret" must now run the cleanup first
        foreach (var ins in m2.Body.Instructions) if (ins.Operand == ret) ins.Operand = first;
        foreach (var eh in m2.Body.ExceptionHandlers) { if (eh.HandlerEnd == ret) eh.HandlerEnd = first; if (eh.TryEnd == ret) eh.TryEnd = first; }
        il.InsertBefore(ret, first);
        il.InsertBefore(ret, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Callvirt, game.ImportReference(clear)));
        il.InsertBefore(ret, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Call, collect));
        n2++;
    }
    game.Write(args[2]);
    Console.WriteLine($"loadclear: {n2} exits of loadDataToLocations now clear the deserialized locations");
}
