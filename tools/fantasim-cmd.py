#!/usr/bin/env python3
import argparse
import json
import os
import sys
import urllib.error
import urllib.request


DEFAULT_BIND = "127.0.0.1:19292"


def base_url():
    bind = os.environ.get("FANTASIM_REMOTE_BIND", DEFAULT_BIND)
    if bind.startswith("http://") or bind.startswith("https://"):
        return bind.rstrip("/")
    return "http://" + bind.rstrip("/")


def request_json(method, path, body=None):
    data = None
    headers = {"Accept": "application/json"}
    if body is not None:
        data = json.dumps(body, separators=(",", ":")).encode("utf-8")
        headers["Content-Type"] = "application/json"

    token = os.environ.get("FANTASIM_REMOTE_TOKEN")
    if token:
        headers["Authorization"] = "Bearer " + token

    request = urllib.request.Request(
        base_url() + path,
        data=data,
        headers=headers,
        method=method)

    try:
        with urllib.request.urlopen(request) as response:
            return response.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        body_text = exc.read().decode("utf-8", errors="replace")
        if body_text:
            print(body_text)
        print("HTTP " + str(exc.code), file=sys.stderr)
        raise SystemExit(1)


def print_json(text):
    try:
        parsed = json.loads(text)
    except json.JSONDecodeError:
        print(text)
        return
    print(json.dumps(parsed, indent=2, sort_keys=True))


def run_health(args):
    print_json(request_json("GET", "/health"))


def run_status(args):
    print_json(request_json("GET", "/status"))


def run_cmd(args):
    body = {"command": args.command_id}
    if args.payload_json is not None:
        try:
            parsed_payload = json.loads(args.payload_json)
        except json.JSONDecodeError as exc:
            raise SystemExit("payloadJson is not valid JSON: " + str(exc))
        body["payloadJson"] = json.dumps(parsed_payload, separators=(",", ":"))
    print_json(request_json("POST", "/command", body))


def build_parser():
    parser = argparse.ArgumentParser(
        description="Drive the FantaSim remote command ingress.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    health = subparsers.add_parser("health", help="Fetch remote health.")
    health.set_defaults(func=run_health)

    status = subparsers.add_parser("status", help="Fetch command status.")
    status.set_defaults(func=run_status)

    cmd = subparsers.add_parser("cmd", help="Run a command.")
    cmd.add_argument("command_id")
    cmd.add_argument("payload_json", nargs="?")
    cmd.set_defaults(func=run_cmd)

    return parser


def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    args.func(args)


if __name__ == "__main__":
    main()
