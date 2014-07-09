﻿#region Copyright 2014 Exceptionless

// This program is free software: you can redistribute it and/or modify it 
// under the terms of the GNU Affero General Public License as published 
// by the Free Software Foundation, either version 3 of the License, or 
// (at your option) any later version.
// 
//     http://www.gnu.org/licenses/agpl-3.0.html

#endregion

using System;
using System.Linq;
using System.Net;
using System.Web.Http;
using AutoMapper;
using Exceptionless.Api.Controllers;
using Exceptionless.Api.Models;
using Exceptionless.Core.Authorization;
using Exceptionless.Core.Billing;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Repositories;
using Exceptionless.Models;
using Exceptionless.Models.Admin;

namespace Exceptionless.App.Controllers.API {
    [RoutePrefix(API_PREFIX + "/tokens")]
    [Authorize(Roles = AuthorizationRoles.User)]
    public class TokenController : RepositoryApiController<ITokenRepository, Token, ViewToken, NewToken, Token> {
        private readonly IApplicationRepository _applicationRepository;
        private readonly IOrganizationRepository _organizationRepository; 
        private readonly IProjectRepository _projectRepository;
        private readonly BillingManager _billingManager;

        public TokenController(ITokenRepository repository, IApplicationRepository applicationRepository, IOrganizationRepository organizationRepository, IProjectRepository projectRepository, BillingManager billingManager) : base(repository) {
            _applicationRepository = applicationRepository;
            _organizationRepository = organizationRepository;
            _projectRepository = projectRepository;
            _billingManager = billingManager;
        }

        #region CRUD
    
        [HttpGet]
        [Route("~/" + API_PREFIX + "/organizations/{organizationId:objectid}/tokens")]
        public IHttpActionResult GetByOrganization(string organizationId, string before = null, string after = null, int limit = 10) {
            if (String.IsNullOrEmpty(organizationId) || !CanAccessOrganization(organizationId))
                return NotFound();

            var options = new PagingOptions { Before = before, After = after, Limit = limit };
            var results = _repository.GetByTypeAndOrganizationId(TokenType.Access, organizationId, options);
            return OkWithResourceLinks(results, options.HasMore, e => e.Id);
        }

        [HttpGet]
        [Route("~/" + API_PREFIX + "/projects/{projectId:objectid}/tokens")]
        public IHttpActionResult GetByProject(string projectId, string before = null, string after = null, int limit = 10) {
            if (String.IsNullOrEmpty(projectId))
                return NotFound();

            var project = _projectRepository.GetById(projectId);
            if (project == null || !CanAccessOrganization(project.OrganizationId))
                return NotFound();

            var options = new PagingOptions { Before = before, After = after, Limit = limit };
            var results = _repository.GetByTypeAndProjectId(TokenType.Access, projectId, options);
            return OkWithResourceLinks(results, options.HasMore, e => e.Id);
        }

        [HttpGet]
        [Route("~/" + API_PREFIX + "/projects/{projectId:objectid}/tokens/default")]
        public IHttpActionResult GetDefaultToken(string projectId) {
            if (String.IsNullOrEmpty(projectId))
                return NotFound();

            var project = _projectRepository.GetById(projectId);
            if (project == null || !CanAccessOrganization(project.OrganizationId))
                return NotFound();

            var token = _repository.GetByTypeAndProjectId(TokenType.Access, projectId, new PagingOptions { Limit = 1 }).FirstOrDefault();
            if (token != null)
                return Ok(Mapper.Map<Token, ViewToken>(token));

            return Post(new NewToken { OrganizationId = project.OrganizationId, ProjectId = projectId});
        }

        [HttpGet]
        [Route("{id:objectid}", Name = "GetTokenById")]
        public override IHttpActionResult GetById(string id) {
            return base.GetById(id);
        }

        // TODO: Verify that we can create new tokens at an org or 
        // project level with specified scopes (Client, User, Admin).
        [Route]
        [HttpPost]
        public override IHttpActionResult Post(NewToken value) {
            return base.Post(value);
        }

        [HttpDelete]
        [Route("{id:objectid}")]
        public override IHttpActionResult Delete(string id) {
            return base.Delete(id);
        }

        #endregion

        protected override Token GetModel(string id, bool useCache = true) {
            var model = base.GetModel(id);
            return model != null && model.Type == TokenType.Access && IsInProject(model.ProjectId) ? model : null;
        }

        protected override PermissionResult CanAdd(Token value) {
            if (String.IsNullOrEmpty(value.OrganizationId))
                return PermissionResult.DenyWithResult(BadRequest());

            // TODO: Code review this.
            if (value.Scopes.Contains("admin") && !User.IsInRole(AuthorizationRoles.GlobalAdmin))
                return PermissionResult.DenyWithResult(BadRequest());

            Project project = _projectRepository.GetById(value.ProjectId, true);
            if (!IsInProject(project))
                return PermissionResult.DenyWithResult(BadRequest());

            if (!String.IsNullOrEmpty(value.ApplicationId)) {
                var application = _applicationRepository.GetById(value.ApplicationId, true);
                if (application == null || !IsInOrganization(application.OrganizationId))
                    return PermissionResult.DenyWithResult(BadRequest());
            }

            return base.CanAdd(value);
        }

        protected override PermissionResult CanDelete(Token value) {
            if (!IsInProject(value.ProjectId))
                return PermissionResult.DenyWithResult(BadRequest());

            return base.CanDelete(value);
        }

        private bool IsInProject(string projectId) {
            if (String.IsNullOrEmpty(projectId))
                return false;

            return IsInProject(_projectRepository.GetById(projectId, true));
        }

        private bool IsInProject(Project value) {
            if (value == null)
                return false;

            return IsInOrganization(value.OrganizationId);
        }
    }
}