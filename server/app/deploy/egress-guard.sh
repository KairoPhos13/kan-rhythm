#!/bin/sh
# Keeps the kan! containers away from everything but the internet.
#
# Docker lets a container reach any address the host can route to. On this
# host that includes the Nebula overlay (the owner's machines), the AWS VPC,
# the EC2 metadata service and the host's own ports (ssh). A compromised
# container must reach none of them: it may only answer through the tunnel and
# talk to Cloudflare (tunnel edge, R2).
#
# Rules go in DOCKER-USER (Docker evaluates it first and never rewrites it) and
# INPUT. Idempotent; run at boot after docker (kan-egress-guard.service).
set -eu

NEBULA_NET="${NEBULA_NET:-54.54.0.0/24}"
VPC_NET="${VPC_NET:-172.31.0.0/16}"
# The VPC's DNS resolver (VPC base + 2): Docker's embedded DNS forwards the
# containers' lookups to it, so port 53 there stays open.
VPC_DNS="${VPC_DNS:-172.31.0.2}"

add() { iptables -C "$@" 2>/dev/null || iptables -I "$@"; }

for dst in 169.254.0.0/16 "$NEBULA_NET" "$VPC_NET" 10.0.0.0/8 192.168.0.0/16; do
  add DOCKER-USER -i br-+ -d "$dst" -j DROP
done
# Name lookups through the VPC resolver only (inserted after the drops, so before them).
for proto in udp tcp; do
  add DOCKER-USER -i br-+ -d "$VPC_DNS" -p "$proto" --dport 53 -j RETURN
done
# Replies to traffic the containers started stay allowed (inserted last, so first).
add DOCKER-USER -i br-+ -m conntrack --ctstate RELATED,ESTABLISHED -j RETURN
# Nothing from a container to the host itself (sshd, nebula). Docker's DNS for
# the containers runs inside their own namespace, so this does not break it.
add INPUT -i br-+ -j DROP
add INPUT -i docker0 -j DROP
echo "kan egress guard: active"
