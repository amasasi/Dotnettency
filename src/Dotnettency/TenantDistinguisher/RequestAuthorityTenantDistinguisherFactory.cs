﻿using Microsoft.AspNetCore.Http;

namespace Dotnettency
{
    public class RequestAuthorityTenantDistinguisherFactory<TTenant> : HttpContextTenantDistinguisherFactory<TTenant>
         where TTenant : class
    {
        public RequestAuthorityTenantDistinguisherFactory(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
        {
        }

        protected override TenantDistinguisher GetTenantDistinguisher(HttpContext context)
        {
            var uri = context.Request.GetAuthorityUri();
            // identify requests by authority only
         //   uri.
            var identity = new TenantDistinguisher(uri);
            return identity;
        }
    }
}