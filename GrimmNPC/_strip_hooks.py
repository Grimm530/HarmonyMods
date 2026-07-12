import re
from pathlib import Path
p = Path(r"c:\!2XRUST\.cursor\HarmonyMods\GrimmNPC\GrimmNPC.cs")
t = p.read_text(encoding="utf-8")
n_before = len(re.findall(r"\[HookMethod", t))
t2 = re.sub(r'\[HookMethod(?:Attribute)?\("[^"]*"\)\]\s*\r?\n[ \t]*', "", t)
t2 = t2.replace("using HookMethod = GrimmNPC.HookMethodAttribute;\n", "")
t2 = t2.replace("using HookMethod = GrimmNPC.HookMethodAttribute;\r\n", "")
n_after = len(re.findall(r"\[HookMethod", t2))
p.write_text(t2, encoding="utf-8")
print(f"HookMethod attrs: {n_before} -> {n_after}")
