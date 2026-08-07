import argparse
import pathlib
import sys

from rembg import new_session, remove


def main() -> int:
    parser = argparse.ArgumentParser(description="Photomotive local vehicle background removal worker")
    parser.add_argument("input", help="Input JPEG/PNG/TIFF path")
    parser.add_argument("output", help="Output transparent PNG path")
    parser.add_argument("--model", default="u2net", help="rembg model name")
    args = parser.parse_args()

    input_path = pathlib.Path(args.input)
    output_path = pathlib.Path(args.output)

    if not input_path.exists():
        print(f"Input file not found: {input_path}", file=sys.stderr)
        return 2

    output_path.parent.mkdir(parents=True, exist_ok=True)

    try:
        session = new_session(args.model)
        source = input_path.read_bytes()
        result = remove(source, session=session)
        output_path.write_bytes(result)
        print(str(output_path))
        return 0
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
