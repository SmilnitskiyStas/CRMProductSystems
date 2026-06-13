#!/bin/bash
# setup-ssl.sh — obtain Let's Encrypt certificate for agrusystems.pp.ua
# Run once on the server after DNS propagation is confirmed.
# Prerequisites: nginx running, port 80 open.

set -e

DOMAIN="agrusystems.pp.ua"
EMAIL="stassmilnitskiy@gmail.com"
WEBROOT="/var/www/certbot"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TEMP_CONF="/etc/nginx/conf.d/shelfguard-acme.conf"

echo "=== ShelfGuard SSL Setup ==="

# Install certbot if not present
if ! command -v certbot &>/dev/null; then
    echo ">>> Installing certbot..."
    apt-get update -qq
    apt-get install -y certbot
fi

# Create webroot directory for ACME challenges
mkdir -p "$WEBROOT"

# Phase 1: temporary HTTP-only config just for ACME challenge
# (shelfguard.conf has HTTPS blocks that require certs to exist first)
echo ">>> Phase 1: deploying temporary HTTP config for ACME challenge..."
cat > "$TEMP_CONF" << EOF
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN www.$DOMAIN api.$DOMAIN;
    location /.well-known/acme-challenge/ {
        root $WEBROOT;
    }
    location / {
        return 301 https://\$host\$request_uri;
    }
}
EOF

nginx -t
systemctl reload nginx

echo ">>> Obtaining certificate for $DOMAIN and www.$DOMAIN and api.$DOMAIN..."
certbot certonly \
    --webroot \
    --webroot-path "$WEBROOT" \
    --email "$EMAIL" \
    --agree-tos \
    --no-eff-email \
    -d "$DOMAIN" \
    -d "www.$DOMAIN" \
    -d "api.$DOMAIN"

# Phase 2: remove temp config, deploy full HTTPS config
echo ">>> Phase 2: enabling full HTTPS config..."
rm -f "$TEMP_CONF"
cp "$SCRIPT_DIR/shelfguard.conf" /etc/nginx/sites-available/shelfguard
ln -sf /etc/nginx/sites-available/shelfguard /etc/nginx/sites-enabled/shelfguard

nginx -t
systemctl reload nginx

echo "=== SSL setup complete ==="
echo "Web:  https://$DOMAIN"
echo "API:  https://api.$DOMAIN"

# Add certbot auto-renewal cron if not already present
if ! crontab -l 2>/dev/null | grep -q "certbot renew"; then
    echo ">>> Adding certbot auto-renewal cron (daily 3:30 AM)..."
    (crontab -l 2>/dev/null; echo "30 3 * * * certbot renew --quiet --deploy-hook 'systemctl reload nginx'") | crontab -
    echo ">>> Cron added."
fi

echo "=== Done ==="
