// Headless Ghidra helper for documenting Backyard Baseball PS2 camera/player positioning.
// Run with -postScript DumpBackyardCamera.java <output-file>.

import java.io.File;
import java.io.PrintWriter;
import java.util.Locale;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.mem.MemoryAccessException;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class DumpBackyardCamera extends GhidraScript {
    private static final String[] TERMS = {
        "battercam", "pitchercam", "fieldingview", "setbattingview",
        "positioncam", "commentatorcam", "setbestcamera", "lookatball",
        "lookatplayer", "baseballcamera", "setposition", "startposition",
        "homeplate", "pitchermound", "positionai", "updatebaseposition",
        "fieldpositions", "initialposition", "sinit_baseballplayer"
    };

    @Override
    public void run() throws Exception {
        String[] arguments = getScriptArgs();
        if (arguments.length != 1) {
            throw new IllegalArgumentException("Expected output file path.");
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.toggleCCode(true);
        decompiler.toggleSyntaxTree(true);
        if (!decompiler.openProgram(currentProgram)) {
            throw new IllegalStateException("Could not initialize the decompiler.");
        }

        int count = 0;
        try (PrintWriter writer = new PrintWriter(new File(arguments[0]))) {
            writer.println("Program: " + currentProgram.getName());
            writer.println("Image base: " + currentProgram.getImageBase());
            writer.println();
            for (Function function : currentProgram.getFunctionManager().getFunctions(true)) {
                String name = function.getName(true);
                String lower = name.toLowerCase(Locale.ROOT);
                if (!matches(lower)) continue;

                count++;
                writer.println("================================================================================");
                writer.println(name + " @ " + function.getEntryPoint());
                writer.println("================================================================================");
                DecompileResults results = decompiler.decompileFunction(function, 90, monitor);
                if (results.decompileCompleted() && results.getDecompiledFunction() != null) {
                    writer.println(results.getDecompiledFunction().getC());
                } else {
                    writer.println("DECOMPILE FAILED: " + results.getErrorMessage());
                }
                writer.println();
            }
            writer.println("================================================================================");
            writer.println("POSITION DATA SYMBOLS");
            writer.println("================================================================================");
            Memory memory = currentProgram.getMemory();
            SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
            while (symbols.hasNext()) {
                Symbol symbol = symbols.next();
                String lower = symbol.getName(true).toLowerCase(Locale.ROOT);
                if (!(lower.contains("fieldpositions") || lower.contains("initialposition") ||
                      lower.contains("infieldpositions") || lower.contains("outfieldpositions"))) continue;
                writer.println(symbol.getName(true) + " @ " + symbol.getAddress());
                try {
                    for (int offset = 0; offset < 192 && memory.contains(symbol.getAddress().add(offset)); offset += 12) {
                        int x = memory.getInt(symbol.getAddress().add(offset));
                        int y = memory.getInt(symbol.getAddress().add(offset + 4));
                        int z = memory.getInt(symbol.getAddress().add(offset + 8));
                        writer.printf(Locale.ROOT, "+0x%02X  %12.4f %12.4f %12.4f  [%08X %08X %08X]%n",
                            offset, Float.intBitsToFloat(x), Float.intBitsToFloat(y), Float.intBitsToFloat(z), x, y, z);
                    }
                } catch (MemoryAccessException exception) {
                    writer.println("(uninitialized/BSS data; values are populated at runtime)");
                }
                ReferenceIterator references = currentProgram.getReferenceManager().getReferencesTo(symbol.getAddress());
                while (references.hasNext()) {
                    Reference reference = references.next();
                    Function owner = currentProgram.getFunctionManager().getFunctionContaining(reference.getFromAddress());
                    writer.println("xref " + reference.getFromAddress() + "  " +
                        (owner == null ? "(no function)" : owner.getName(true)));
                }
                writer.println();
            }
            writer.println("Matched functions: " + count);
        } finally {
            decompiler.dispose();
        }
        println("Camera analysis exported; matched " + count + " functions.");
    }

    private static boolean matches(String name) {
        for (String term : TERMS) {
            if (name.contains(term)) return true;
        }
        return false;
    }
}
