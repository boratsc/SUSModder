#!/bin/bash

# ============================================================================
# Migration Runner Script
# Purpose: Execute SQL migrations for Compatibility Matrix
# Usage: ./run-migrations.sh [migration_number]
# ============================================================================

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
DB_HOST="193.70.42.86"
DB_USER="susfuckr"
DB_PASS="TXyF7re10wo2JlTYBzcp8t3b9PDtbRLX"
DB_NAME="susfuckr"
MIGRATIONS_DIR="/srv/synapsekit-boracik/migrations"
BACKUP_DIR="/srv/synapsekit-boracik/backups"

# Functions
print_header() {
    echo -e "${BLUE}============================================================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}============================================================================${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

# Create backup before migration
create_backup() {
    print_info "Creating database backup..."
    
    mkdir -p "$BACKUP_DIR"
    
    TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
    BACKUP_FILE="${BACKUP_DIR}/${DB_NAME}_pre_migration_${TIMESTAMP}.sql"
    
    docker exec nginx-mysql mysqldump \
        -h "$DB_HOST" \
        -u "$DB_USER" \
        -p"$DB_PASS" \
        --single-transaction \
        --routines \
        --triggers \
        "$DB_NAME" > "$BACKUP_FILE" 2>/dev/null
    
    if [ $? -eq 0 ]; then
        gzip "$BACKUP_FILE"
        print_success "Backup created: ${BACKUP_FILE}.gz"
        echo "$BACKUP_FILE.gz" > "${BACKUP_DIR}/.last_backup"
    else
        print_error "Backup failed!"
        exit 1
    fi
}

# Run a single migration file
run_migration() {
    local migration_file=$1
    
    print_info "Running migration: $(basename $migration_file)"
    
    docker exec nginx-mysql mysql \
        -h "$DB_HOST" \
        -u "$DB_USER" \
        -p"$DB_PASS" \
        "$DB_NAME" < "$migration_file" 2>&1 | grep -v "Warning"
    
    if [ ${PIPESTATUS[0]} -eq 0 ]; then
        print_success "Migration completed: $(basename $migration_file)"
        return 0
    else
        print_error "Migration failed: $(basename $migration_file)"
        return 1
    fi
}

# Rollback to last backup
rollback() {
    print_warning "Rolling back to last backup..."
    
    if [ -f "${BACKUP_DIR}/.last_backup" ]; then
        LAST_BACKUP=$(cat "${BACKUP_DIR}/.last_backup")
        
        print_info "Restoring from: $LAST_BACKUP"
        
        gunzip -c "$LAST_BACKUP" | docker exec -i nginx-mysql mysql \
            -h "$DB_HOST" \
            -u "$DB_USER" \
            -p"$DB_PASS" \
            "$DB_NAME" 2>/dev/null
        
        if [ $? -eq 0 ]; then
            print_success "Rollback completed successfully!"
        else
            print_error "Rollback failed!"
            exit 1
        fi
    else
        print_error "No backup found for rollback!"
        exit 1
    fi
}

# List available migrations
list_migrations() {
    print_header "Available Migrations"
    
    for file in "$MIGRATIONS_DIR"/*.sql; do
        if [ -f "$file" ]; then
            echo "  - $(basename $file)"
        fi
    done
}

# Test database connection
test_connection() {
    print_info "Testing database connection..."
    
    docker exec nginx-mysql mysql \
        -h "$DB_HOST" \
        -u "$DB_USER" \
        -p"$DB_PASS" \
        -e "SELECT 1" "$DB_NAME" &>/dev/null
    
    if [ $? -eq 0 ]; then
        print_success "Database connection successful!"
        return 0
    else
        print_error "Database connection failed!"
        return 1
    fi
}

# Main execution
main() {
    print_header "🚀 Compatibility Matrix - Migration Runner"
    
    # Test connection first
    if ! test_connection; then
        exit 1
    fi
    
    # Parse arguments
    case "$1" in
        "list")
            list_migrations
            exit 0
            ;;
        "rollback")
            rollback
            exit 0
            ;;
        "all")
            print_info "Running all migrations..."
            
            # Create backup
            create_backup
            
            # Run all migrations in order
            for migration in "$MIGRATIONS_DIR"/*.sql; do
                if [ -f "$migration" ]; then
                    if ! run_migration "$migration"; then
                        print_error "Migration failed! Rolling back..."
                        rollback
                        exit 1
                    fi
                fi
            done
            
            print_success "All migrations completed successfully!"
            ;;
        [0-9]*)
            MIGRATION_NUM=$(printf "%03d" $1)
            MIGRATION_FILE="${MIGRATIONS_DIR}/${MIGRATION_NUM}_*.sql"
            
            if ls $MIGRATION_FILE 1> /dev/null 2>&1; then
                create_backup
                
                for file in $MIGRATION_FILE; do
                    if ! run_migration "$file"; then
                        print_error "Migration failed! Rolling back..."
                        rollback
                        exit 1
                    fi
                done
                
                print_success "Migration $MIGRATION_NUM completed successfully!"
            else
                print_error "Migration $MIGRATION_NUM not found!"
                exit 1
            fi
            ;;
        *)
            echo "Usage: $0 [command]"
            echo ""
            echo "Commands:"
            echo "  all              - Run all migrations"
            echo "  [number]         - Run specific migration (e.g., 1, 001)"
            echo "  list             - List available migrations"
            echo "  rollback         - Rollback to last backup"
            echo ""
            echo "Examples:"
            echo "  $0 all           - Run all migrations"
            echo "  $0 1             - Run migration 001"
            echo "  $0 list          - Show available migrations"
            echo "  $0 rollback      - Rollback last migration"
            exit 1
            ;;
    esac
}

# Run main function
main "$@"
