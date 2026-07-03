# -*- mode: python ; coding: utf-8 -*-
# Build: pyinstaller emg_backend.spec
# Output: dist/emg_backend/emg_backend.exe

from PyInstaller.utils.hooks import collect_all

numpy_datas, numpy_binaries, numpy_hidden = collect_all("numpy")
scipy_datas, scipy_binaries, scipy_hidden = collect_all("scipy")

a = Analysis(
    ["emg_backend_entry.py"],
    pathex=[],
    binaries=numpy_binaries + scipy_binaries,
    datas=[
        ("Python Code/emg_core.py", "."),
    ]
    + numpy_datas
    + scipy_datas,
    hiddenimports=[
        "bitalino",
        "emg_core",
    ]
    + numpy_hidden
    + scipy_hidden,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="emg_backend",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    upx_exclude=[],
    name="emg_backend",
)
