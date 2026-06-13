#!/bin/bash
# setup-ssl.sh — obtain Let's Encrypt certificate for agrusystems.pp.ua
# Run once on the server after DNS propagation is confirmed.
# Prerequisites: nginx running, port 80 open, webroot dir exists.

set -e

DOMAIN="agrusystems.pp.ua"
EMAIL="stassmilnitskiy@gmail.com"
WEBROOT="/var/www/certbot"

echo "=== ShelfGuard SSL Setup ==="

# Install certbot if not present
if ! command -v certbot &>/dev/null; then
    echo ">>> Installing certbot..."
    apt-get update -qq
    apt-get install -y certbot
fi

# Create webroot directory for ACME challenges
mkdir -p "$WEBROOT"

# Deploy nginx config (HTTP-only first pass is already in shelfguard.conf)
echo ">>> Deploying nginx config..."
cp "$(dirname "$0")/shelfguard.conf" /etc/nginx/sites-available/shelfguard
ln -sf /etc/nginx/sites-available/shelfguard /etc/nginx/sites-enabled/shelfguard

# Test and reload nginx
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

echo ">>> Certificate obtained. Reloading nginx with SSL config..."
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
