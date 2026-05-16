-- ============================================================
-- TravelAgencyDB - Database Migrations
-- SQL Server
--
-- Each migration is idempotent: safe to run multiple times.
-- Applied migrations are recorded in __MigrationHistory.
-- Run this file against the target server to bring the
-- database up to date from any state.
-- ============================================================

-- ── Bootstrap ────────────────────────────────────────────────
IF DB_ID('TravelAgencyDB') IS NULL
BEGIN
    CREATE DATABASE TravelAgencyDB;
END
GO

USE TravelAgencyDB;
GO

IF OBJECT_ID('dbo.__MigrationHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.__MigrationHistory (
        MigrationId   NVARCHAR(100) NOT NULL PRIMARY KEY,
        AppliedAt     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
        Description   NVARCHAR(500) NOT NULL
    );
END
GO

-- ── Helper: run a migration only once ────────────────────────
-- Each block follows this pattern:
--   IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = '<V###>')
--   BEGIN ... INSERT INTO dbo.__MigrationHistory ... END
-- ─────────────────────────────────────────────────────────────


-- ============================================================
-- V001 - Initial schema: Trips, Users, Bookings, WaitingList, Payments
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V001')
BEGIN
    -- Trips
    IF OBJECT_ID('dbo.Trips', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Trips (
            TripId          INT            IDENTITY PRIMARY KEY,
            Destination     NVARCHAR(100)  NOT NULL,
            Country         NVARCHAR(100)  NOT NULL,
            StartDate       DATE           NOT NULL,
            EndDate         DATE           NOT NULL,
            Price           DECIMAL(10,2)  NOT NULL,
            AvailableRooms  INT            NOT NULL,
            Category        NVARCHAR(50)   NULL,
            MinAge          INT            NULL,
            Description     NVARCHAR(MAX)  NULL,
            ImagePath       NVARCHAR(MAX)  NULL
        );
    END

    -- Users
    IF OBJECT_ID('dbo.Users', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Users (
            UserId          INT            IDENTITY PRIMARY KEY,
            FirstName       NVARCHAR(50)   NOT NULL,
            LastName        NVARCHAR(50)   NOT NULL,
            NationalId      NVARCHAR(20)   NOT NULL DEFAULT '',
            Email           NVARCHAR(100)  NOT NULL UNIQUE,
            PasswordHash    NVARCHAR(255)  NOT NULL,
            Role            NVARCHAR(20)   NOT NULL DEFAULT 'User',
            Status          NVARCHAR(20)   NOT NULL DEFAULT 'Active',
            CreditCardNumber NVARCHAR(20)  NOT NULL DEFAULT '',
            CardExpiryDate  DATE           NULL,
            Cvc             NVARCHAR(4)    NULL
        );
    END

    -- Bookings
    IF OBJECT_ID('dbo.Bookings', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Bookings (
            BookingId    INT            IDENTITY PRIMARY KEY,
            UserId       INT            NOT NULL,
            TripId       INT            NOT NULL,
            BookingDate  DATETIME       NOT NULL DEFAULT GETDATE(),
            Status       NVARCHAR(20)   NOT NULL,   -- 'Active' | 'Cancelled'

            CONSTRAINT FK_Booking_User FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId),
            CONSTRAINT FK_Booking_Trip FOREIGN KEY (TripId) REFERENCES dbo.Trips(TripId)
        );
    END

    -- WaitingList
    IF OBJECT_ID('dbo.WaitingList', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.WaitingList (
            WaitingId  INT       IDENTITY PRIMARY KEY,
            TripId     INT       NOT NULL,
            UserId     INT       NOT NULL,
            JoinDate   DATETIME  NOT NULL DEFAULT GETDATE(),

            CONSTRAINT FK_Waiting_Trip FOREIGN KEY (TripId) REFERENCES dbo.Trips(TripId),
            CONSTRAINT FK_Waiting_User FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId),
            CONSTRAINT UQ_Wait         UNIQUE (TripId, UserId)
        );
    END

    -- Payments
    IF OBJECT_ID('dbo.Payments', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Payments (
            PaymentId    INT            IDENTITY PRIMARY KEY,
            BookingId    INT            NOT NULL,
            Amount       DECIMAL(10,2)  NOT NULL,
            PaymentDate  DATETIME       NOT NULL DEFAULT GETDATE(),
            Status       NVARCHAR(20)   NOT NULL,   -- 'Success' | 'Failed'

            CONSTRAINT FK_Payment_Booking FOREIGN KEY (BookingId) REFERENCES dbo.Bookings(BookingId)
        );
    END

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V001', 'Initial schema: Trips, Users, Bookings, WaitingList, Payments');
END
GO


-- ============================================================
-- V002 - Bookings: add IsPaid, PaidAt
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V002')
BEGIN
    IF COL_LENGTH('dbo.Bookings', 'IsPaid') IS NULL
        ALTER TABLE dbo.Bookings ADD IsPaid BIT NOT NULL DEFAULT 0;

    IF COL_LENGTH('dbo.Bookings', 'PaidAt') IS NULL
        ALTER TABLE dbo.Bookings ADD PaidAt DATETIME2 NULL;

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V002', 'Bookings: add IsPaid, PaidAt');
END
GO


-- ============================================================
-- V003 - Trips: add OldPrice, DiscountEndDate
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V003')
BEGIN
    IF COL_LENGTH('dbo.Trips', 'OldPrice') IS NULL
        ALTER TABLE dbo.Trips ADD OldPrice DECIMAL(10,2) NULL;

    IF COL_LENGTH('dbo.Trips', 'DiscountEndDate') IS NULL
        ALTER TABLE dbo.Trips ADD DiscountEndDate DATETIME NULL;

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V003', 'Trips: add OldPrice, DiscountEndDate for discount tracking');
END
GO


-- ============================================================
-- V004 - Users: add Status
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V004')
BEGIN
    IF COL_LENGTH('dbo.Users', 'Status') IS NULL
        ALTER TABLE dbo.Users ADD Status NVARCHAR(20) NOT NULL DEFAULT 'Active';

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V004', 'Users: add Status (Active | Blocked)');
END
GO


-- ============================================================
-- V005 - Create Reviews table
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V005')
BEGIN
    IF OBJECT_ID('dbo.Reviews', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Reviews (
            ReviewId   INT            IDENTITY(1,1) PRIMARY KEY,
            TripId     INT            NOT NULL,
            UserId     INT            NOT NULL,
            Rating     INT            NOT NULL
                           CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
            Comment    NVARCHAR(500)  NULL,
            CreatedAt  DATETIME       NOT NULL DEFAULT GETDATE(),

            CONSTRAINT FK_Reviews_Trip FOREIGN KEY (TripId) REFERENCES dbo.Trips(TripId),
            CONSTRAINT FK_Reviews_User FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
        );
    END

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V005', 'Create Reviews table (per-trip ratings)');
END
GO


-- ============================================================
-- V006 - Create TripImages table
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V006')
BEGIN
    IF OBJECT_ID('dbo.TripImages', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.TripImages (
            ImageId    INT             IDENTITY(1,1) PRIMARY KEY,
            TripId     INT             NOT NULL,
            ImagePath  NVARCHAR(500)   NOT NULL,

            CONSTRAINT FK_TripImages_Trip FOREIGN KEY (TripId) REFERENCES dbo.Trips(TripId)
        );
    END

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V006', 'Create TripImages table (gallery images per trip)');
END
GO


-- ============================================================
-- V007 - Create PasswordResets table
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V007')
BEGIN
    IF OBJECT_ID('dbo.PasswordResets', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.PasswordResets (
            Id        INT             IDENTITY(1,1) PRIMARY KEY,
            Email     NVARCHAR(200)   NOT NULL,
            Token     NVARCHAR(200)   NOT NULL,
            ExpireAt  DATETIME        NOT NULL
        );
    END

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V007', 'Create PasswordResets table');
END
GO


-- ============================================================
-- V008 - WaitingList: add NotifiedAt, ExpirationAt
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V008')
BEGIN
    IF COL_LENGTH('dbo.WaitingList', 'NotifiedAt') IS NULL
        ALTER TABLE dbo.WaitingList ADD NotifiedAt DATETIME NULL;

    IF COL_LENGTH('dbo.WaitingList', 'ExpirationAt') IS NULL
        ALTER TABLE dbo.WaitingList ADD ExpirationAt DATETIME NULL;

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V008', 'WaitingList: add NotifiedAt, ExpirationAt');
END
GO


-- ============================================================
-- V009 - Create SiteReviews table
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V009')
BEGIN
    IF OBJECT_ID('dbo.SiteReviews', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.SiteReviews (
            ReviewId   INT            IDENTITY PRIMARY KEY,
            UserId     INT            NOT NULL,
            Rating     INT            NOT NULL
                           CONSTRAINT CK_SiteReviews_Rating CHECK (Rating BETWEEN 1 AND 5),
            Comment    NVARCHAR(500)  NULL,
            CreatedAt  DATETIME       NOT NULL DEFAULT GETDATE(),

            CONSTRAINT FK_SiteReviews_User FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
        );
    END

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V009', 'Create SiteReviews table (company-level ratings)');
END
GO


-- ============================================================
-- V010 - Trips: add CancellationDays
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V010')
BEGIN
    IF COL_LENGTH('dbo.Trips', 'CancellationDays') IS NULL
        ALTER TABLE dbo.Trips
            ADD CancellationDays INT NOT NULL
                CONSTRAINT DF_Trips_CancellationDays DEFAULT 0;

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V010', 'Trips: add CancellationDays (cancellation policy)');
END
GO


-- ============================================================
-- V011 - Bookings: add Quantity
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V011')
BEGIN
    IF COL_LENGTH('dbo.Bookings', 'Quantity') IS NULL
        ALTER TABLE dbo.Bookings ADD Quantity INT NOT NULL DEFAULT 1;

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V011', 'Bookings: add Quantity (number of rooms/people per booking)');
END
GO


-- ============================================================
-- V012 - Trips: add Quantity
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V012')
BEGIN
    IF COL_LENGTH('dbo.Trips', 'Quantity') IS NULL
        ALTER TABLE dbo.Trips ADD Quantity INT NOT NULL DEFAULT 1;

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V012', 'Trips: add Quantity multiplier');
END
GO


-- ============================================================
-- V013 - Bookings: add GroupMinAge with CHECK constraint
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V013')
BEGIN
    IF COL_LENGTH('dbo.Bookings', 'GroupMinAge') IS NULL
        ALTER TABLE dbo.Bookings ADD GroupMinAge INT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_Bookings_GroupMinAge_Range'
    )
    BEGIN
        ALTER TABLE dbo.Bookings WITH CHECK
            ADD CONSTRAINT CK_Bookings_GroupMinAge_Range
            CHECK (GroupMinAge IS NULL OR (GroupMinAge BETWEEN 0 AND 120));
    END

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V013', 'Bookings: add GroupMinAge (0-120) with CHECK constraint');
END
GO


-- ============================================================
-- V014 - Trips: add PackageName, back-fill from Destination + Category
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V014')
BEGIN
    IF COL_LENGTH('dbo.Trips', 'PackageName') IS NULL
        ALTER TABLE dbo.Trips
            ADD PackageName NVARCHAR(200) NOT NULL
                CONSTRAINT DF_Trips_PackageName DEFAULT '';

    -- Back-fill rows that have no package name yet
    UPDATE dbo.Trips
    SET PackageName = LTRIM(RTRIM(
        CONCAT(
            ISNULL(Destination, ''),
            CASE WHEN ISNULL(Category, '') <> ''
                 THEN CONCAT(' ', Category) ELSE '' END,
            ' Package'
        )
    ))
    WHERE LTRIM(RTRIM(PackageName)) = '';

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V014', 'Trips: add PackageName with back-fill from Destination + Category');
END
GO


-- ============================================================
-- V015 - Trips: add IsHidden flag
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.__MigrationHistory WHERE MigrationId = 'V015')
BEGIN
    IF COL_LENGTH('dbo.Trips', 'IsHidden') IS NULL
        ALTER TABLE dbo.Trips
            ADD IsHidden BIT NOT NULL
                CONSTRAINT DF_Trips_IsHidden DEFAULT 0;

    INSERT INTO dbo.__MigrationHistory (MigrationId, Description)
    VALUES ('V015', 'Trips: add IsHidden flag (hide trip from public gallery)');
END
GO


-- ============================================================
-- Done — show applied migrations
-- ============================================================
SELECT MigrationId, AppliedAt, Description
FROM   dbo.__MigrationHistory
ORDER  BY MigrationId;
GO
