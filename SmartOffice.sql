CREATE DATABASE SmartOffice;
GO

USE SmartOffice;
GO

CREATE TABLE Admin
(
    AdminId     INT IDENTITY(1,1) PRIMARY KEY,
    Username    VARCHAR(50)  NOT NULL UNIQUE,
    Password    VARCHAR(255) NOT NULL,
    FullName    VARCHAR(150) NOT NULL,
    DateCreated DATETIME DEFAULT(GETDATE())
);


INSERT INTO Admin (Username, Password, FullName)
VALUES ('admin', '53d6316bd7b9044e6bb5deaa87fe8316c2fde3938b78f8448875b08e551ccc95', 'System Administrator');

SELECT * FROM Admin;

CREATE TABLE Appointment
(
    AppointmentId     INT IDENTITY(1,1) PRIMARY KEY,
    ApplicantId       INT NOT NULL FOREIGN KEY REFERENCES ApplicantRegister(ApplicantId),
    Purpose           VARCHAR(255) NOT NULL,
    AppointmentDate   DATE NOT NULL,
    AppointmentTime   TIME NOT NULL,
    Status            VARCHAR(20) NOT NULL DEFAULT('Pending'), -- Pending, Approved, Completed, Rejected
    DateRequested     DATETIME DEFAULT(GETDATE())               -- this is what the graph will count
);

ALTER TABLE Appointment
ADD ContactNumber   VARCHAR(20)   NULL,
    Email           VARCHAR(150)  NULL,
    ResumeFile      NVARCHAR(255) NULL,
    AdditionalNotes VARCHAR(500)  NULL;
GO

ALTER TABLE Appointment
ADD ValidIDFile NVARCHAR(255) NULL;
ALTER TABLE Appointment ADD DateApproved DATETIME NULL;

UPDATE Appointment
SET DateApproved = DateRequested
WHERE Status IN ('Approved', 'Completed') AND DateApproved IS NULL;

ALTER TABLE Appointment
ADD DateCompleted DATETIME NULL;

SELECT * FROM Appointment;    


CREATE TABLE ApplicantRegister
    (
        ApplicantId     INT IDENTITY(1,1) PRIMARY KEY,
        FullName        VARCHAR(150)  NOT NULL,
        Username        VARCHAR(50)   NOT NULL UNIQUE,
        Email           VARCHAR(150)  NOT NULL UNIQUE,
        ContactNumber   VARCHAR(20)   NOT NULL,
        Birthdate       DATE          NOT NULL,
        InvitedBy       VARCHAR(100)  NULL,
        Password        VARCHAR(255)  NOT NULL,
        ProfileImage    NVARCHAR(255) NULL,
        DateRegistered  DATETIME      DEFAULT(GETDATE())
    );

ALTER TABLE ApplicantRegister
ADD ResetCode VARCHAR (6) NULL,
    ResetCodeExpiry DATETIME NULL;
GO

SELECT * FROM ApplicantRegister;

DELETE FROM ApplicantRegister;
DBCC CHECKIDENT ('ApplicantRegister', RESEED, 0);
 