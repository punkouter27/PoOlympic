"""LFS-aware file check for tests that need a shipped brain."""

from pathlib import Path

LFS_POINTER = b"version https://git-lfs.github.com/spec"


def brain_present(path: Path) -> bool:
    """The ONNX file is here with its content (a CI checkout without LFS holds only a pointer)."""
    if not path.exists():
        return False
    with path.open("rb") as f:
        return not f.read(len(LFS_POINTER)).startswith(LFS_POINTER)
