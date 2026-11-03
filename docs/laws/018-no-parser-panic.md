---
id: L018
title: The Bayan parser never panics and keeps every span inside the input and its parent
status: proposed
kind: property
scope: selfhost/parser/parser.byn
proof: tests/Bayan.Tests/SelfhostParserNoPanicLawTests.cs
check: dotnet test -v q --filter "Law=L018"
timeout: 1800
---

## Statement
For every byte sequence, `bayan-parse` terminates without a panic. Every node span lies within the input, and every child span lies within its parent's.

## Counterexample looks like
`bayan-parse --many` over a directory whose input ends in a dropped value prints `span out of range: errorExpr 267+0 outside 260+6`, or any input on which `bayan-parse` exits 134 with `panic:`.

## Proof notes
Proof: `tests/Bayan.Tests/SelfhostParserNoPanicLawTests.cs`, one `--many` process per corpus (not one
per input), built once via `SelfhostParserTests.Built`. Two corpora, both run through `--many`:

- the corrupted seed corpus (`SelfhostParserGenerator.GenerateCorrupt(Seeds)`), 128 inputs;
- a random-byte corpus (4,000 inputs, XorShift-seeded, parse-golden fragments mixed with invalid UTF-8,
  parser specials, capped nesting and byte mutations).

Every input is also parsed with the C# parser and its tree walked by a reflection span checker mirroring
`spans.byn` (`CSharpSpanViolations`), so the port is asserted against C#, never against a hard-coded list.

The first clause (exit 0 or 1, no `panic:`) and the "node span within the input" half hold over both
corpora: `--many` exits 0 and prints no `span out of range` line without ` outside `. The
child-within-parent half is false of C# itself, so it cannot hold for a port that matches C#:

- 30 of 128 corrupted inputs and 1,085 of 4,000 random inputs have a C# child outside its parent (measured 2026-11-03; 28 and 1,056 when T18 first measured). All
  come from the known C# span bugs: `move`, `fail`, `break`/`continue` with a label, `except` and `?.`
  take their span before the child (Q473, work/todo.md), and a dropped value's `ErrorExpr` is built at
  `Current.Start`, outside its parent statement (work/todo.md). The port's `checkSpans` reports exactly
  those inputs and no others; the test asserts the port's child-outside-parent set is a subset of C#'s.

  The 30 corrupted inputs are: `corrupt-valid-AssignStmt.byn`, `corrupt-valid-BecauseClause.byn`, `corrupt-valid-BinaryPattern.byn`, `corrupt-valid-CatchExpr.byn`, `corrupt-valid-CollectionExpr.byn`, `corrupt-valid-ErrorExpr.byn`, `corrupt-valid-ErrorVariant.byn`, `corrupt-valid-FieldInit.byn`, `corrupt-valid-ForStmt.byn`, `corrupt-valid-ImportDecl.byn`, `corrupt-valid-IncDecStmt.byn`, `corrupt-valid-IsExpr.byn`, `corrupt-valid-LabeledStmt.byn`, `corrupt-valid-MapEntryExpr.byn`, `corrupt-valid-PositionalPattern.byn`, `corrupt-valid-PropertySubpattern.byn`, `corrupt-valid-RelationalPattern.byn`, `corrupt-valid-SwitchArm.byn`, `corrupt-valid-SwitchLabel.byn`, `corrupt-valid-TargetMemberExpr.byn`, `corrupt-valid-TypeAliasDecl.byn`, `corrupt-valid-VarPattern.byn`, `corrupt-valid-WhileLetStmt.byn`, `corrupt-valid-seed-000.byn`, `corrupt-valid-seed-001.byn`, `corrupt-valid-seed-002.byn`, `corrupt-valid-seed-003.byn`, `corrupt-valid-seed-004.byn`, `corrupt-valid-seed-005.byn`, `corrupt-valid-seed-007.byn`.

  The 1,085 random inputs (seed `0x9E3779B97F4A7C15`) are listed by index under *Counterexample inputs* below.

- No candidate crashes the C# parser (2026-11-03: `random corpus: 0 candidates crash the C# parser`). Until
  then 2 did, `0x9E3779B97F4A7C15 02184`
  (`270A6C65742078203D2020800A65207B7D0A20202020696620287320202E20`) and
  `0x9E3779B97F4A7C15 02482`
  (`7320697320436972636C65207B7D0A20202020696620287320697320436972636C20C3C820`): `Parser.ReportBraceless`
  threw `ArgumentOutOfRangeException` on a braceless body that took no token (Q486), and the port panicked
  identically (`slice out of range` at `lexer/source.byn:80`), so the generator replaced both with empty
  inputs. C# was fixed in 517d1c9 and the port in 010 T44; both inputs are now in the no-panic corpus, and
  both are among the 1,085 child-outside-parent inputs below. The generator still replaces a candidate that
  crashes C#, and logs the count.

The no-panic clause and the "node span within the input" half now hold on every input of both corpora,
with nothing replaced. The child-within-parent half is still false of C# itself (the span bugs above), so
the law stays `proposed` on that count (Q509); enactment is scheduled for the start of 011 (Q744).

The generator caps block/expression nesting at `SelfhostParserGenerator.NestingCap` (8); the random
corpus caps its own nesting the same way. Both `Parser.cs` and the port recurse without a limit, so a
deep enough input overflows the stack in both; the proof does not exercise stack overflow (Q508).

Mutation: `selfhost/parser/parser.byn:105` `spanFrom` changed `prevEnd > start ? prevEnd : start` to
`prevEnd > start ? prevEnd + 1 : start` (a one-byte span extension). `BayanParseNeverPanicsAndEverySpanMatchesTheCSharpTree`
failed (`random: exit 134`, `panic: offset out of range` at `lexer/source.byn:42`). Restored from
`tmp/scratch/parser-backup.byn` with `cp`, `cmp` clean, and the rerun passed.

## Counterexample inputs

Random inputs (seed `0x9E3779B97F4A7C15`) whose C# tree has a child outside its parent, 1085 indices (2026-11-03):

00010, 00013, 00020, 00026, 00027, 00031, 00035, 00041, 00042, 00043, 00048, 00052, 00053, 00056, 00059, 00063, 00064, 00065, 00068, 00071, 00076, 00085, 00091, 00102, 00105, 00113, 00115, 00118, 00122, 00123, 00132, 00133, 00134, 00140, 00150, 00156, 00157, 00158, 00159, 00161, 00162, 00165, 00167, 00170, 00173, 00177, 00179, 00180, 00192, 00196, 00203, 00208, 00216, 00217, 00225, 00227, 00233, 00235, 00244, 00254, 00255, 00257, 00259, 00264, 00269, 00273, 00278, 00279, 00281, 00288, 00290, 00291, 00293, 00299, 00303, 00306, 00308, 00310, 00312, 00314, 00325, 00328, 00334, 00337, 00339, 00354, 00357, 00359, 00361, 00365, 00367, 00370, 00371, 00380, 00383, 00388, 00397, 00399, 00410, 00416, 00418, 00419, 00424, 00425, 00426, 00428, 00433, 00434, 00437, 00439, 00442, 00444, 00445, 00456, 00458, 00461, 00462, 00463, 00466, 00476, 00479, 00481, 00486, 00488, 00491, 00494, 00495, 00497, 00502, 00504, 00505, 00510, 00514, 00518, 00520, 00521, 00526, 00529, 00532, 00534, 00536, 00538, 00540, 00541, 00542, 00545, 00556, 00558, 00561, 00565, 00568, 00569, 00571, 00572, 00577, 00578, 00580, 00581, 00584, 00591, 00593, 00594, 00599, 00603, 00606, 00608, 00610, 00615, 00619, 00625, 00627, 00628, 00631, 00633, 00636, 00639, 00641, 00644, 00645, 00649, 00651, 00654, 00656, 00659, 00661, 00665, 00668, 00670, 00677, 00685, 00692, 00696, 00698, 00701, 00704, 00705, 00708, 00714, 00718, 00722, 00727, 00728, 00736, 00740, 00741, 00745, 00747, 00753, 00758, 00759, 00761, 00763, 00764, 00766, 00771, 00774, 00775, 00777, 00786, 00787, 00792, 00794, 00795, 00799, 00802, 00803, 00805, 00806, 00810, 00813, 00814, 00823, 00826, 00830, 00831, 00835, 00837, 00839, 00845, 00849, 00854, 00856, 00857, 00861, 00891, 00901, 00907, 00912, 00914, 00915, 00916, 00917, 00920, 00923, 00924, 00925, 00930, 00931, 00932, 00933, 00936, 00938, 00939, 00940, 00942, 00950, 00953, 00959, 00962, 00965, 00966, 00967, 00972, 00977, 00986, 00991, 00992, 00994, 00996, 00997, 00999, 01013, 01015, 01016, 01017, 01019, 01020, 01026, 01028, 01029, 01030, 01033, 01034, 01038, 01039, 01045, 01054, 01055, 01056, 01057, 01063, 01069, 01074, 01076, 01078, 01079, 01080, 01081, 01082, 01094, 01107, 01109, 01110, 01111, 01113, 01119, 01126, 01129, 01133, 01139, 01143, 01152, 01154, 01158, 01162, 01166, 01170, 01180, 01181, 01182, 01186, 01195, 01196, 01199, 01201, 01202, 01205, 01218, 01220, 01227, 01230, 01231, 01232, 01233, 01236, 01239, 01253, 01256, 01257, 01259, 01262, 01264, 01265, 01269, 01271, 01280, 01281, 01286, 01287, 01289, 01292, 01293, 01298, 01300, 01304, 01311, 01315, 01316, 01317, 01321, 01322, 01323, 01324, 01331, 01333, 01339, 01341, 01347, 01354, 01358, 01359, 01362, 01364, 01366, 01367, 01368, 01385, 01388, 01390, 01393, 01394, 01395, 01396, 01397, 01398, 01408, 01409, 01411, 01417, 01421, 01422, 01425, 01427, 01432, 01436, 01437, 01451, 01454, 01460, 01464, 01468, 01472, 01473, 01476, 01477, 01478, 01479, 01481, 01484, 01487, 01496, 01499, 01506, 01507, 01510, 01513, 01514, 01515, 01517, 01520, 01525, 01533, 01536, 01538, 01542, 01544, 01549, 01551, 01552, 01556, 01564, 01567, 01577, 01599, 01603, 01605, 01606, 01611, 01612, 01638, 01639, 01647, 01657, 01658, 01662, 01665, 01684, 01688, 01694, 01697, 01698, 01700, 01703, 01704, 01705, 01711, 01715, 01717, 01719, 01720, 01734, 01735, 01736, 01739, 01740, 01742, 01744, 01748, 01751, 01756, 01758, 01759, 01763, 01769, 01771, 01773, 01777, 01778, 01786, 01799, 01801, 01806, 01811, 01818, 01822, 01823, 01825, 01829, 01831, 01838, 01842, 01843, 01844, 01849, 01850, 01853, 01855, 01872, 01873, 01875, 01876, 01880, 01884, 01893, 01900, 01903, 01907, 01910, 01912, 01913, 01919, 01920, 01921, 01922, 01923, 01926, 01927, 01928, 01929, 01935, 01937, 01938, 01949, 01950, 01957, 01960, 01961, 01966, 01971, 01974, 01975, 01979, 01989, 01996, 01997, 02004, 02006, 02008, 02009, 02015, 02026, 02028, 02029, 02035, 02037, 02041, 02051, 02053, 02058, 02060, 02065, 02066, 02074, 02080, 02084, 02095, 02101, 02102, 02103, 02112, 02113, 02114, 02115, 02120, 02125, 02130, 02131, 02136, 02148, 02150, 02152, 02156, 02158, 02161, 02164, 02165, 02167, 02168, 02171, 02174, 02175, 02177, 02184, 02190, 02191, 02192, 02197, 02201, 02203, 02204, 02205, 02211, 02216, 02226, 02228, 02230, 02232, 02238, 02239, 02241, 02244, 02245, 02251, 02253, 02255, 02256, 02264, 02266, 02271, 02272, 02275, 02287, 02297, 02301, 02304, 02306, 02309, 02310, 02316, 02318, 02319, 02322, 02323, 02326, 02327, 02330, 02334, 02342, 02350, 02353, 02354, 02358, 02361, 02364, 02369, 02370, 02371, 02372, 02375, 02382, 02384, 02386, 02387, 02395, 02399, 02401, 02407, 02410, 02413, 02424, 02431, 02436, 02439, 02442, 02444, 02445, 02447, 02448, 02450, 02452, 02453, 02456, 02459, 02464, 02465, 02467, 02470, 02471, 02472, 02482, 02487, 02493, 02497, 02511, 02518, 02520, 02521, 02522, 02525, 02537, 02544, 02550, 02555, 02556, 02560, 02561, 02563, 02567, 02573, 02577, 02587, 02589, 02590, 02596, 02605, 02612, 02615, 02628, 02634, 02635, 02636, 02637, 02638, 02639, 02646, 02651, 02654, 02655, 02658, 02659, 02661, 02662, 02668, 02669, 02675, 02677, 02679, 02685, 02686, 02687, 02688, 02691, 02694, 02698, 02700, 02703, 02707, 02711, 02713, 02714, 02717, 02724, 02727, 02729, 02740, 02742, 02743, 02751, 02768, 02769, 02775, 02776, 02777, 02788, 02790, 02792, 02801, 02804, 02805, 02813, 02814, 02821, 02824, 02825, 02829, 02830, 02836, 02838, 02846, 02848, 02850, 02852, 02854, 02856, 02861, 02862, 02865, 02868, 02873, 02876, 02878, 02879, 02880, 02881, 02888, 02893, 02895, 02898, 02900, 02901, 02904, 02907, 02910, 02916, 02920, 02922, 02927, 02932, 02933, 02934, 02944, 02950, 02951, 02953, 02956, 02958, 02959, 02966, 02968, 02970, 02974, 02977, 02983, 02991, 02994, 02998, 03001, 03006, 03008, 03010, 03014, 03024, 03025, 03027, 03041, 03048, 03050, 03059, 03060, 03064, 03074, 03077, 03080, 03093, 03097, 03100, 03103, 03104, 03105, 03106, 03107, 03110, 03112, 03113, 03116, 03126, 03128, 03140, 03142, 03145, 03155, 03162, 03169, 03173, 03181, 03187, 03189, 03194, 03195, 03201, 03202, 03203, 03208, 03223, 03224, 03225, 03229, 03231, 03234, 03236, 03237, 03246, 03247, 03252, 03258, 03260, 03263, 03264, 03268, 03288, 03294, 03295, 03296, 03298, 03305, 03309, 03313, 03318, 03323, 03329, 03330, 03331, 03335, 03336, 03340, 03341, 03345, 03347, 03348, 03351, 03353, 03354, 03355, 03359, 03361, 03364, 03370, 03372, 03374, 03376, 03386, 03388, 03391, 03393, 03400, 03401, 03404, 03407, 03410, 03411, 03424, 03425, 03437, 03440, 03442, 03444, 03446, 03450, 03454, 03459, 03466, 03469, 03472, 03473, 03475, 03478, 03480, 03486, 03488, 03490, 03492, 03493, 03494, 03497, 03501, 03507, 03508, 03509, 03515, 03518, 03524, 03525, 03528, 03532, 03534, 03540, 03542, 03547, 03550, 03555, 03560, 03563, 03565, 03567, 03570, 03571, 03576, 03579, 03582, 03586, 03596, 03599, 03604, 03606, 03609, 03625, 03630, 03631, 03634, 03636, 03640, 03644, 03646, 03649, 03652, 03655, 03656, 03657, 03658, 03662, 03663, 03666, 03668, 03671, 03681, 03684, 03689, 03697, 03698, 03700, 03704, 03716, 03717, 03730, 03731, 03733, 03738, 03740, 03741, 03747, 03748, 03759, 03760, 03761, 03765, 03768, 03769, 03772, 03773, 03774, 03777, 03781, 03785, 03789, 03793, 03798, 03800, 03802, 03810, 03813, 03827, 03835, 03840, 03842, 03843, 03845, 03852, 03853, 03856, 03857, 03859, 03860, 03861, 03863, 03864, 03869, 03872, 03873, 03875, 03876, 03883, 03884, 03889, 03890, 03894, 03897, 03898, 03902, 03904, 03905, 03907, 03917, 03919, 03922, 03923, 03929, 03930, 03941, 03944, 03946, 03952, 03954, 03955, 03957, 03958, 03959, 03960, 03961, 03969, 03971, 03984, 03991, 03993, 03994, 03999
