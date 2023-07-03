using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class AbpUser
    {
        public AbpUser()
        {
            InverseCreatorUser = new HashSet<AbpUser>();
            InverseDeleterUser = new HashSet<AbpUser>();
            InverseLastModifierUser = new HashSet<AbpUser>();
        }

        public long Id { get; set; }
        public DateTime CreationTime { get; set; }
        public long? CreatorUserId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public long? LastModifierUserId { get; set; }
        public bool IsDeleted { get; set; }
        public long? DeleterUserId { get; set; }
        public DateTime? DeletionTime { get; set; }
        public string AuthenticationSource { get; set; }
        public string UserName { get; set; }
        public int? TenantId { get; set; }
        public string EmailAddress { get; set; }
        public string Name { get; set; }
        public string Surname { get; set; }
        public string Password { get; set; }
        public string EmailConfirmationCode { get; set; }
        public string PasswordResetCode { get; set; }
        public DateTime? LockoutEndDateUtc { get; set; }
        public int AccessFailedCount { get; set; }
        public bool IsLockoutEnabled { get; set; }
        public string PhoneNumber { get; set; }
        public bool IsPhoneNumberConfirmed { get; set; }
        public string SecurityStamp { get; set; }
        public bool IsTwoFactorEnabled { get; set; }
        public bool IsEmailConfirmed { get; set; }
        public bool IsActive { get; set; }
        public string NormalizedUserName { get; set; }
        public string NormalizedEmailAddress { get; set; }
        public string ConcurrencyStamp { get; set; }
        public Guid? ProfilePictureId { get; set; }
        public bool ShouldChangePasswordOnNextLogin { get; set; }
        public DateTime? SignInTokenExpireTimeUtc { get; set; }
        public string SignInToken { get; set; }
        public string GoogleAuthenticatorKey { get; set; }

        public virtual AbpUser CreatorUser { get; set; }
        public virtual AbpUser DeleterUser { get; set; }
        public virtual AbpUser LastModifierUser { get; set; }
        public virtual ICollection<AbpUser> InverseCreatorUser { get; set; }
        public virtual ICollection<AbpUser> InverseDeleterUser { get; set; }
        public virtual ICollection<AbpUser> InverseLastModifierUser { get; set; }
    }
}
