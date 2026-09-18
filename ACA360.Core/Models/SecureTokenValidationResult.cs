using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Models
{
    public class SecureTokenValidationResult
    {
        public int? EmployeeId { get; set; }
        public int? Year { get; set; }

        /// <summary>
        /// True only when both values are present. Callers read <see cref="EmployeeId"/>
        /// and <see cref="Year"/> straight after testing this, so requiring both is what
        /// makes that safe — checking EmployeeId alone left Year.Value able to throw.
        /// SecureDownloadLinks.TaxYear is NOT NULL, so this cannot currently trigger;
        /// the point is that the guarantee now matches the name.
        /// </summary>
        public bool IsValid => EmployeeId.HasValue && Year.HasValue;

        /// <summary>
        /// Outcome from sp_ValidateSecureToken. 0 succeeded, 1 invalid, 2 locked.
        ///
        /// Unknown link, expired link, revoked link and wrong digits all return 1 on
        /// purpose: telling them apart would confirm which links exist and whose they
        /// are. 2 is safe to disclose because reaching it means the caller already
        /// holds a real link.
        /// </summary>
        public int Status { get; set; }

        /// <summary>Minutes until the lock clears. Meaningful only when Status is 2.</summary>
        public int RetryAfterMinutes { get; set; }

        /// <summary>The link exists and is real, but is refusing attempts for now.</summary>
        public bool IsLockedOut => Status == 2;
    }
}