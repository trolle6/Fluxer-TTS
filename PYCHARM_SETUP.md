# PyCharm Setup for FluxerTools

If you get `ModuleNotFoundError: No module named 'fluxer'`, PyCharm is using the wrong Python interpreter.

## Fix in 3 steps

1. **File** → **Settings** (or `Ctrl+Alt+S`)
2. **Project: FluxerTools** → **Python Interpreter**
3. Click the gear icon → **Add...** → **Existing** → Browse to:
   ```
   C:\Users\simon\PycharmProjects\FluxerTools\.venv\Scripts\python.exe
   ```
4. Click **OK** → **Apply**

## If FluxerTools isn't the project

If you opened a parent folder (e.g. `PycharmProjects`) and FluxerTools is just a subfolder:

- **Option A:** Open FluxerTools as its own project: **File** → **Open** → select `FluxerTools` folder
- **Option B:** Add the FluxerTools venv as an interpreter, then edit your Run Configuration: **Run** → **Edit Configurations** → set **Python interpreter** to `FluxerTools\.venv`

## Quick run (no PyCharm)

Double-click `run.bat` in the FluxerTools folder.
